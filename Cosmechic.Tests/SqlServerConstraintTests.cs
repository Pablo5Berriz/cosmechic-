using Cosmechic.Models;
using Cosmechic.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Cosmechic.Tests
{
    // COSMECHIC-DATA-001 (section 25) : vérifie, contre un vrai SQL Server 2022 jetable
    // (pas InMemory), que les CHECK constraints, l'index unique et le comportement
    // rowversion introduits par ce lot sont réellement appliqués par le moteur de base de
    // données — pas seulement déclarés côté modèle EF Core. Se désactive proprement
    // (chaque test retourne sans assertion, avec un message explicite dans la sortie) si
    // Docker n'est pas disponible dans l'environnement d'exécution ; voir SqlServerFixture.
    [Collection("SqlServerFixture collection")]
    public class SqlServerConstraintTests
    {
        private readonly SqlServerFixture _fixture;
        private readonly ITestOutputHelper _output;

        public SqlServerConstraintTests(SqlServerFixture fixture, ITestOutputHelper output)
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

        private static async Task<int> SeedCategoryAsync(CosmechicsContext context)
        {
            var category = new Category
            {
                Nom = $"Categorie-{Guid.NewGuid():N}",
                Image = "categorie.jpg",
                Disponible = true,
            };
            context.Categories.Add(category);
            await context.SaveChangesAsync();
            return category.CategorieId;
        }

        [Fact]
        public async Task Produit_NegativeStock_IsRejectedByCheckConstraint()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var categorieId = await SeedCategoryAsync(context);

            context.Produits.Add(new Produit
            {
                Nom = "Produit stock invalide",
                CategorieId = categorieId,
                Prix = 10.00m,
                Stock = -1,
                Disponible = true,
                Image = "produit.jpg",
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        [Fact]
        public async Task Produit_NegativePrix_IsRejectedByCheckConstraint()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var categorieId = await SeedCategoryAsync(context);

            context.Produits.Add(new Produit
            {
                Nom = "Produit prix invalide",
                CategorieId = categorieId,
                Prix = -5.00m,
                Stock = 1,
                Disponible = true,
                Image = "produit.jpg",
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        [Fact]
        public async Task ShoppingCart_ZeroOrNegativeCount_IsRejectedByCheckConstraint()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var categorieId = await SeedCategoryAsync(context);

            var produit = new Produit
            {
                Nom = "Produit panier",
                CategorieId = categorieId,
                Prix = 10.00m,
                Stock = 5,
                Disponible = true,
                Image = "produit.jpg",
            };
            context.Produits.Add(produit);
            await context.SaveChangesAsync();

            context.ShoppingCarts.Add(new ShoppingCart
            {
                ProduitId = produit.ProduitId,
                Count = 0,
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        [Fact]
        public async Task ProcessedStripeEvent_DuplicateStripeEventId_IsRejectedByUniqueIndex()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var stripeEventId = $"evt_{Guid.NewGuid():N}";

            context.ProcessedStripeEvents.Add(new ProcessedStripeEvent
            {
                StripeEventId = stripeEventId,
                EventType = "checkout.session.completed",
                ReceivedAt = DateTime.UtcNow,
                ProcessingStatus = "Received",
            });
            await context.SaveChangesAsync();

            context.ProcessedStripeEvents.Add(new ProcessedStripeEvent
            {
                StripeEventId = stripeEventId,
                EventType = "checkout.session.completed",
                ReceivedAt = DateTime.UtcNow,
                ProcessingStatus = "Received",
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        [Fact]
        public async Task Produit_RowVersion_IsGeneratedByDatabaseAndChangesOnUpdate()
        {
            if (SkipIfUnavailable()) return;

            using var writeContext = _fixture.CreateBusinessContext();
            var categorieId = await SeedCategoryAsync(writeContext);

            var produit = new Produit
            {
                Nom = "Produit rowversion",
                CategorieId = categorieId,
                Prix = 10.00m,
                Stock = 5,
                Disponible = true,
                Image = "produit.jpg",
            };
            writeContext.Produits.Add(produit);
            await writeContext.SaveChangesAsync();

            var initialRowVersion = produit.RowVersion.ToArray();
            Assert.NotEmpty(initialRowVersion);

            using (var updateContext = _fixture.CreateBusinessContext())
            {
                var toUpdate = await updateContext.Produits.SingleAsync(p => p.ProduitId == produit.ProduitId);
                toUpdate.Stock = 4;
                await updateContext.SaveChangesAsync();

                Assert.NotEqual(initialRowVersion, toUpdate.RowVersion);
            }
        }

        // COSMECHIC-COMMERCE-OPERATIONS-001A (section 45/47) : l'invariant financier
        // Subtotal + ShippingAmount + TaxAmount - DiscountAmount = OrderTotal est vérifié
        // par une contrainte CHECK au niveau moteur (CK_OrderHeaders_Total_Equals_Components),
        // pas seulement par le code C# — une ligne incohérente doit être rejetée même en
        // écrivant directement en base.
        private static async Task<string> SeedCustomerAsync(CosmechicsContext context)
        {
            var userId = $"user-{Guid.NewGuid():N}";
            context.AspNetUsers.Add(new AspNetUser { Id = userId, UserName = userId, Email = $"{userId}@test.local" });
            await context.SaveChangesAsync();
            return userId;
        }

        private static OrderHeader BuildOrderHeader(string userId, decimal subtotal, decimal shipping, decimal tax, decimal discount, decimal orderTotal) => new()
        {
            ApplicationUserId = userId,
            OrderDate = DateTime.UtcNow,
            ShippingDate = DateTime.UtcNow,
            Subtotal = subtotal,
            ShippingAmount = shipping,
            TaxAmount = tax,
            DiscountAmount = discount,
            OrderTotal = orderTotal,
            OrderStatus = "Pending",
            PaymentStatus = "Pending",
            PhoneNumber = "5145551234",
            StreetAddress = "1 rue Test",
            City = "Montreal",
            State = "QC",
            PostalCode = "H0H0H0",
            Name = "Test",
            PaymentDate = DateTime.UtcNow,
            PaymentDueDate = DateTime.UtcNow,
        };

        [Fact]
        public async Task OrderHeader_InconsistentTotal_IsRejectedByCheckConstraint()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var userId = await SeedCustomerAsync(context);

            // 50 + 15 + 7.49 - 0 = 72.49, mais OrderTotal prétend 0.01 : doit être rejeté.
            context.OrderHeaders.Add(BuildOrderHeader(userId, subtotal: 50.00m, shipping: 15.00m, tax: 7.49m, discount: 0m, orderTotal: 0.01m));

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        [Fact]
        public async Task OrderHeader_ConsistentTotal_IsAccepted()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var userId = await SeedCustomerAsync(context);

            context.OrderHeaders.Add(BuildOrderHeader(userId, subtotal: 50.00m, shipping: 15.00m, tax: 7.49m, discount: 0m, orderTotal: 72.49m));

            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task ShippingMethod_InactiveMethod_CanStillBeReferencedByHistoricalOrder()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var userId = await SeedCustomerAsync(context);

            var method = new ShippingMethod { Name = $"Méthode-{Guid.NewGuid():N}", Price = 15.00m, IsActive = true, SortOrder = 1 };
            context.ShippingMethods.Add(method);
            await context.SaveChangesAsync();

            var order = BuildOrderHeader(userId, subtotal: 10.00m, shipping: 15.00m, tax: 0m, discount: 0m, orderTotal: 25.00m);
            order.ShippingMethodId = method.ShippingMethodId;
            order.ShippingMethodName = method.Name;
            context.OrderHeaders.Add(order);
            await context.SaveChangesAsync();

            // La méthode est désactivée après coup (ne devrait jamais casser l'historique :
            // FK_OrderHeaders_ShippingMethods en Restrict, jamais Cascade).
            method.IsActive = false;
            await context.SaveChangesAsync();

            var reloadedOrder = await context.OrderHeaders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            Assert.Equal(method.ShippingMethodId, reloadedOrder.ShippingMethodId);
        }

        [Fact]
        public async Task Produit_Prix_RoundTripsThroughMoneyColumn()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var categorieId = await SeedCategoryAsync(context);

            var produit = new Produit
            {
                Nom = "Produit prix money",
                CategorieId = categorieId,
                Prix = 12345.6789m,
                Stock = 1,
                Disponible = true,
                Image = "produit.jpg",
            };
            context.Produits.Add(produit);
            await context.SaveChangesAsync();

            var reloaded = await context.Produits.AsNoTracking()
                .SingleAsync(p => p.ProduitId == produit.ProduitId);

            Assert.Equal(12345.6789m, reloaded.Prix);
        }
    }
}
