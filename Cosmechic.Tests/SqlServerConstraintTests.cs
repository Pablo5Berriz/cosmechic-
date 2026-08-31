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
