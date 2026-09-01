using Cosmechic.Data;
using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Cosmechic.Tests
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 34/40/71/72) : garanties de concurrence
    // réellement vérifiées contre SQL Server (pas InMemory) — remboursement (solde
    // remboursable jamais dépassé) et remise en stock (jamais deux fois pour la même
    // ReturnItem).
    [Collection("SqlServerFixture collection")]
    public class SqlServerRefundAndRestockConcurrencyTests
    {
        private readonly SqlServerFixture _fixture;
        private readonly ITestOutputHelper _output;

        public SqlServerRefundAndRestockConcurrencyTests(SqlServerFixture fixture, ITestOutputHelper output)
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

        private async Task<int> SeedPaidOrderAsync(CosmechicsContext context, decimal orderTotal)
        {
            var userId = await SeedUserAsync();
            var order = new OrderHeader
            {
                ApplicationUserId = userId,
                OrderDate = DateTime.UtcNow,
                OrderTotal = orderTotal,
                Subtotal = orderTotal,
                OrderStatus = SD.OrderStatusConfirmed,
                PaymentStatus = SD.PaymentStatusPaid,
                FulfillmentStatus = SD.FulfillmentStatusProcessing,
                PaymentIntentId = "pi_test",
                Name = "Test",
                PhoneNumber = "5145551234",
                StreetAddress = "1 rue Test",
                City = "Montreal",
                State = "QC",
                PostalCode = "H0H0H0",
            };
            context.OrderHeaders.Add(order);
            await context.SaveChangesAsync();
            return order.Id;
        }

        [Fact]
        public async Task RefundConcurrency_Total100_Concurrent80And50_NeverExceeds100()
        {
            if (SkipIfUnavailable()) return;

            using var setupContext = _fixture.CreateBusinessContext();
            var orderId = await SeedPaidOrderAsync(setupContext, 100m);

            using var contextA = _fixture.CreateBusinessContext();
            using var contextB = _fixture.CreateBusinessContext();
            var stripeA = new FakeStripeRefundService();
            var stripeB = new FakeStripeRefundService();
            var serviceA = new RefundOrchestrationService(contextA, stripeA, new OrderLifecycleService(contextA), NullLogger<RefundOrchestrationService>.Instance);
            var serviceB = new RefundOrchestrationService(contextB, stripeB, new OrderLifecycleService(contextB), NullLogger<RefundOrchestrationService>.Instance);

            var taskA = serviceA.RequestRefundAsync(orderId, 80m, null, "refund A", "admin-a", SD.ActorTypeAdmin);
            var taskB = serviceB.RequestRefundAsync(orderId, 50m, null, "refund B", "admin-b", SD.ActorTypeAdmin);
            var results = await Task.WhenAll(taskA, taskB);

            var succeededCount = results.Count(r => r is RefundSucceeded);
            var rejectedCount = results.Count(r => r is RefundRejected);
            // 80 + 50 = 130 > 100 : les deux ne peuvent jamais réussir ensemble. Selon
            // l'ordre d'arrivée, seule la première tient dans le solde (80 OU 50 selon qui
            // gagne la course) — jamais les deux, jamais aucune (au moins une doit réussir
            // puisque chaque montant pris isolément tient dans 100).
            Assert.Equal(1, succeededCount);
            Assert.Equal(1, rejectedCount);

            using var verifyContext = _fixture.CreateBusinessContext();
            var finalRefundedAmount = await verifyContext.OrderHeaders.AsNoTracking()
                .Where(o => o.Id == orderId).Select(o => o.RefundedAmount).SingleAsync();

            Assert.True(finalRefundedAmount <= 100m, $"RefundedAmount ({finalRefundedAmount}) a dépassé OrderTotal (100).");
            Assert.True(finalRefundedAmount == 80m || finalRefundedAmount == 50m);
        }

        [Fact]
        public async Task RestockConcurrency_SameReturnItem_TwoSimultaneousCompletions_StockIncrementsExactlyOnce()
        {
            if (SkipIfUnavailable()) return;

            using var setupContext = _fixture.CreateBusinessContext();
            var category = new Category { Nom = $"Cat-{Guid.NewGuid():N}", Image = "c.jpg", Disponible = true };
            setupContext.Categories.Add(category);
            await setupContext.SaveChangesAsync();

            var produit = new Produit { Nom = $"P-{Guid.NewGuid():N}", CategorieId = category.CategorieId, Prix = 10m, Stock = 5, Disponible = true, Image = "p.jpg" };
            setupContext.Produits.Add(produit);
            await setupContext.SaveChangesAsync();

            var orderId = await SeedPaidOrderAsync(setupContext, 20m);
            var detail = new OrderDetail { OrderHeaderId = orderId, ProduitId = produit.ProduitId, Count = 2, Price = 10m, ProduitNom = "P" };
            setupContext.OrderDetails.Add(detail);
            await setupContext.SaveChangesAsync();

            var returnRequest = new ReturnRequest { OrderId = orderId, ApplicationUserId = "user-x", Status = SD.ReturnStatusReceived, CreatedAt = DateTime.UtcNow };
            setupContext.ReturnRequests.Add(returnRequest);
            await setupContext.SaveChangesAsync();

            var returnItem = new ReturnItem { ReturnRequestId = returnRequest.Id, OrderDetailId = detail.Id, Quantity = 2 };
            setupContext.ReturnItems.Add(returnItem);
            await setupContext.SaveChangesAsync();

            using var contextA = _fixture.CreateBusinessContext();
            using var contextB = _fixture.CreateBusinessContext();
            var serviceA = new RestockService(contextA, NullLogger<RestockService>.Instance);
            var serviceB = new RestockService(contextB, NullLogger<RestockService>.Instance);

            var taskA = serviceA.CompleteRestockAsync(returnItem.Id, "admin-a");
            var taskB = serviceB.CompleteRestockAsync(returnItem.Id, "admin-b");
            var results = await Task.WhenAll(taskA, taskB);

            var completedCount = results.Count(r => r is RestockCompleted);
            Assert.Equal(1, completedCount);

            using var verifyContext = _fixture.CreateBusinessContext();
            var finalStock = await verifyContext.Produits.AsNoTracking().Where(p => p.ProduitId == produit.ProduitId).Select(p => p.Stock).SingleAsync();
            Assert.Equal(7, finalStock);

            var movementCount = await verifyContext.StockMovements.CountAsync(m => m.ProduitId == produit.ProduitId);
            Assert.Equal(1, movementCount);
        }

        [Fact]
        public async Task RefundConsistentWithOrderTotal_CheckConstraint_AcceptsExactBalance()
        {
            if (SkipIfUnavailable()) return;

            using var setupContext = _fixture.CreateBusinessContext();
            var orderId = await SeedPaidOrderAsync(setupContext, 55.55m);

            using var context = _fixture.CreateBusinessContext();
            var stripe = new FakeStripeRefundService();
            var service = new RefundOrchestrationService(context, stripe, new OrderLifecycleService(context), NullLogger<RefundOrchestrationService>.Instance);

            var result = await service.RequestRefundAsync(orderId, 55.55m, null, "full", "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundSucceeded>(result);
            using var verifyContext = _fixture.CreateBusinessContext();
            var order = await verifyContext.OrderHeaders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            Assert.Equal(55.55m, order.RefundedAmount);
            Assert.Equal(SD.PaymentStatusRefunded, order.PaymentStatus);
        }
    }
}
