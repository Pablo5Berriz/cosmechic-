using Cosmechic.Data;
using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe.Checkout;
using Xunit;
using Xunit.Abstractions;

namespace Cosmechic.Tests
{
    // COSMECHIC-ECOM-CORE-001 (section 32) : garanties de concurrence et de transaction
    // réellement vérifiées contre SQL Server (pas InMemory, qui n'applique ni RowVersion
    // ni contrainte UNIQUE avec la même sémantique) — voir SqlServerFixture
    // (COSMECHIC-DATA-001) pour le conteneur jetable partagé par la classe.
    [Collection("SqlServerFixture collection")]
    public class SqlServerFulfillmentConcurrencyTests
    {
        private readonly SqlServerFixture _fixture;
        private readonly ITestOutputHelper _output;

        public SqlServerFulfillmentConcurrencyTests(SqlServerFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        private bool SkipIfUnavailable()
        {
            if (_fixture.IsAvailable)
            {
                return false;
            }

            _output.WriteLine($"Test ignoré (SQL Server jetable indisponible) : {_fixture.SkipReason}");
            return true;
        }

        private async Task<int> SeedCategoryAsync(CosmechicsContext context)
        {
            var category = new Category { Nom = $"Cat-{Guid.NewGuid():N}", Image = "c.jpg", Disponible = true };
            context.Categories.Add(category);
            await context.SaveChangesAsync();
            return category.CategorieId;
        }

        // Contourne le gap connu et documenté (COSMECHIC-DATA-001.md, section 1) :
        // CosmechicsContext.AspNetUser mappe 4 colonnes (StreetAddress/City/State/
        // PostalCode) absentes de la table AspNetUsers réellement créée par la migration
        // Identity de ApplicationDbContext. Corriger cette migration est explicitement
        // hors périmètre de ce lot (Identity/DbContext architecture, section 3 du mandat).
        // On sème donc l'utilisateur via ApplicationDbContext (IdentityDbContext nu, sans
        // ces 4 colonnes), exactement comme le fait la vraie inscription Identity en
        // production — CosmechicsContext ne sert ici qu'à interroger OrderHeaders/Produits.
        private async Task<string> SeedUserAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_fixture.ConnectionString)
                .Options;
            await using var identityContext = new ApplicationDbContext(options);
            var userId = $"user-{Guid.NewGuid():N}";
            identityContext.Users.Add(new IdentityUser { Id = userId, UserName = userId });
            await identityContext.SaveChangesAsync();
            return userId;
        }

        private async Task<int> SeedProduitAsync(CosmechicsContext context, int categorieId, decimal prix, decimal stock)
        {
            var produit = new Produit
            {
                Nom = $"Produit-{Guid.NewGuid():N}",
                CategorieId = categorieId,
                Prix = prix,
                Stock = stock,
                Disponible = true,
                Image = "p.jpg",
            };
            context.Produits.Add(produit);
            await context.SaveChangesAsync();
            return produit.ProduitId;
        }

        private async Task<int> SeedPendingOrderAsync(CosmechicsContext context, int produitId, decimal prix, int count, string sessionId)
        {
            var userId = await SeedUserAsync();
            var order = new OrderHeader
            {
                ApplicationUserId = userId,
                OrderDate = DateTime.UtcNow,
                OrderTotal = prix * count,
                OrderStatus = SD.StatusPending,
                PaymentStatus = SD.PaymentStatusPending,
                SessionId = sessionId,
                Name = "Test",
                PhoneNumber = "5145551234",
                StreetAddress = "1 rue Test",
                City = "Montreal",
                State = "QC",
                PostalCode = "H0H0H0",
            };
            order.OrderDetails.Add(new OrderDetail { ProduitId = produitId, Count = count, Price = prix });
            context.OrderHeaders.Add(order);
            await context.SaveChangesAsync();
            return order.Id;
        }

        private static Session PaidSession(string sessionId, int orderId, long amountTotal)
            => new()
            {
                Id = sessionId,
                PaymentStatus = "paid",
                AmountTotal = amountTotal,
                Currency = "cad",
                PaymentIntentId = "pi_test",
                Metadata = new Dictionary<string, string> { ["OrderId"] = orderId.ToString() },
            };

        [Fact]
        public async Task TestA_ConcurrentFulfillment_SameProduct_StockOne_ExactlyOneSucceeds_NeverNegative()
        {
            if (SkipIfUnavailable()) return;

            using var setupContext = _fixture.CreateBusinessContext();
            var categorieId = await SeedCategoryAsync(setupContext);
            var produitId = await SeedProduitAsync(setupContext, categorieId, prix: 25.00m, stock: 1);

            var orderAId = await SeedPendingOrderAsync(setupContext, produitId, 25.00m, count: 1, sessionId: "cs_test_a");
            var orderBId = await SeedPendingOrderAsync(setupContext, produitId, 25.00m, count: 1, sessionId: "cs_test_b");

            using var contextA = _fixture.CreateBusinessContext();
            using var contextB = _fixture.CreateBusinessContext();
            var serviceA = new StripeFulfillmentService(contextA, NullLogger<StripeFulfillmentService>.Instance);
            var serviceB = new StripeFulfillmentService(contextB, NullLogger<StripeFulfillmentService>.Instance);

            var sessionA = PaidSession("cs_test_a", orderAId, 2500);
            var sessionB = PaidSession("cs_test_b", orderBId, 2500);

            var taskA = serviceA.ProcessCheckoutSessionEventAsync("evt_a", "checkout.session.completed", sessionA);
            var taskB = serviceB.ProcessCheckoutSessionEventAsync("evt_b", "checkout.session.completed", sessionB);
            var results = await Task.WhenAll(taskA, taskB);

            var fulfilledCount = results.Count(r => r.Outcome == FulfillmentOutcome.Fulfilled);
            var stockUnavailableCount = results.Count(r => r.Outcome == FulfillmentOutcome.StockUnavailable);
            Assert.Equal(1, fulfilledCount);
            Assert.Equal(1, stockUnavailableCount);

            using var verifyContext = _fixture.CreateBusinessContext();
            var finalStock = await verifyContext.Produits.AsNoTracking().Where(p => p.ProduitId == produitId).Select(p => p.Stock).SingleAsync();
            Assert.Equal(0, finalStock);
        }

        [Fact]
        public async Task TestB_SameStripeEventId_ProcessedConcurrentlyTwice_UniqueConstraintProtects_SingleEffect()
        {
            if (SkipIfUnavailable()) return;

            using var setupContext = _fixture.CreateBusinessContext();
            var categorieId = await SeedCategoryAsync(setupContext);
            var produitId = await SeedProduitAsync(setupContext, categorieId, prix: 25.00m, stock: 5);
            var orderId = await SeedPendingOrderAsync(setupContext, produitId, 25.00m, count: 1, sessionId: "cs_test_dup");

            using var contextA = _fixture.CreateBusinessContext();
            using var contextB = _fixture.CreateBusinessContext();
            var serviceA = new StripeFulfillmentService(contextA, NullLogger<StripeFulfillmentService>.Instance);
            var serviceB = new StripeFulfillmentService(contextB, NullLogger<StripeFulfillmentService>.Instance);

            var session = PaidSession("cs_test_dup", orderId, 2500);
            var sameEventId = "evt_duplicate_concurrent";

            var taskA = serviceA.ProcessCheckoutSessionEventAsync(sameEventId, "checkout.session.completed", session);
            var taskB = serviceB.ProcessCheckoutSessionEventAsync(sameEventId, "checkout.session.completed", session);
            var results = await Task.WhenAll(taskA, taskB);

            Assert.Equal(1, results.Count(r => r.Outcome == FulfillmentOutcome.Fulfilled));
            Assert.Equal(1, results.Count(r => r.Outcome == FulfillmentOutcome.AlreadyProcessed));

            using var verifyContext = _fixture.CreateBusinessContext();
            var eventRows = await verifyContext.ProcessedStripeEvents.AsNoTracking()
                .Where(e => e.StripeEventId == sameEventId).ToListAsync();
            Assert.Single(eventRows);

            var finalStock = await verifyContext.Produits.AsNoTracking().Where(p => p.ProduitId == produitId).Select(p => p.Stock).SingleAsync();
            // Un seul effet métier : décrémenté une fois (5 -> 4), jamais deux (5 -> 3).
            Assert.Equal(4, finalStock);
        }

        [Fact]
        public async Task TestC_MultiLineOrder_OneLineInsufficientStock_NeitherLineIsMutated()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var categorieId = await SeedCategoryAsync(context);
            var produitOkId = await SeedProduitAsync(context, categorieId, prix: 10.00m, stock: 50);
            var produitInsufficientId = await SeedProduitAsync(context, categorieId, prix: 15.00m, stock: 0);
            var userId = await SeedUserAsync();

            var order = new OrderHeader
            {
                ApplicationUserId = userId,
                OrderDate = DateTime.UtcNow,
                OrderTotal = 25.00m,
                OrderStatus = SD.StatusPending,
                PaymentStatus = SD.PaymentStatusPending,
                SessionId = "cs_test_multiline",
                Name = "Test",
                PhoneNumber = "5145551234",
                StreetAddress = "1 rue Test",
                City = "Montreal",
                State = "QC",
                PostalCode = "H0H0H0",
            };
            order.OrderDetails.Add(new OrderDetail { ProduitId = produitOkId, Count = 1, Price = 10.00m });
            order.OrderDetails.Add(new OrderDetail { ProduitId = produitInsufficientId, Count = 1, Price = 15.00m });
            context.OrderHeaders.Add(order);
            await context.SaveChangesAsync();

            var service = new StripeFulfillmentService(context, NullLogger<StripeFulfillmentService>.Instance);
            var session = PaidSession("cs_test_multiline", order.Id, 2500);

            var result = await service.ProcessCheckoutSessionEventAsync("evt_multiline", "checkout.session.completed", session);

            Assert.Equal(FulfillmentOutcome.StockUnavailable, result.Outcome);

            using var verifyContext = _fixture.CreateBusinessContext();
            // Aucune mutation partielle : le produit AVEC assez de stock n'a pas été
            // décrémenté simplement parce qu'un autre produit de la même commande
            // manquait de stock (atomicité, section 18).
            var stockOk = await verifyContext.Produits.AsNoTracking().Where(p => p.ProduitId == produitOkId).Select(p => p.Stock).SingleAsync();
            Assert.Equal(50, stockOk);
            var stockInsufficient = await verifyContext.Produits.AsNoTracking().Where(p => p.ProduitId == produitInsufficientId).Select(p => p.Stock).SingleAsync();
            Assert.Equal(0, stockInsufficient);
        }
    }
}
