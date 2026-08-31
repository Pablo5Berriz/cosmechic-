using Cosmechic.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-DATA-001 : vérifie, au niveau des métadonnées du modèle EF Core (donc sans
    // dépendre d'un fournisseur SQL Server réel), que les décisions de schéma prises dans ce
    // lot sont bien reflétées par CosmechicsContext.OnModelCreating. Complète les tests
    // d'intégration SQL Server réel de SqlServerConstraintTests, qui vérifient eux le
    // comportement effectif des CHECK constraints / index uniques / rowversion en base.
    public class DataModelMetadataTests
    {
        // Fournisseur SqlServer requis (pas InMemory) : le type mapping InMemory ne sait
        // pas résoudre les métadonnées relationnelles (GetColumnType, etc.), et
        // l'annotation "exclu des migrations" n'existe que dans le modèle design-time.
        // Aucune connexion réelle n'est jamais ouverte : ces tests n'inspectent que le
        // modèle construit par les conventions/annotations, sans appeler Database.*.
        private static CosmechicsContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<CosmechicsContext>()
                .UseSqlServer("Server=metadata-only;Database=metadata-only;TrustServerCertificate=True;")
                .Options;
            return new CosmechicsContext(options);
        }

        [Fact]
        public void CosmechicsContext_Model_BuildsWithoutError()
        {
            using var context = CreateContext();

            var model = context.Model;

            Assert.NotNull(model);
        }

        [Theory]
        [InlineData(typeof(Produit), nameof(Produit.Prix))]
        [InlineData(typeof(OrderDetail), nameof(OrderDetail.Price))]
        [InlineData(typeof(OrderHeader), nameof(OrderHeader.OrderTotal))]
        public void MonetaryColumns_AreConfiguredAsMoney(Type entityClrType, string propertyName)
        {
            using var context = CreateContext();

            var entityType = context.Model.FindEntityType(entityClrType);
            Assert.NotNull(entityType);

            var property = entityType!.FindProperty(propertyName);
            Assert.NotNull(property);
            Assert.Equal("money", property!.GetColumnType());
        }

        [Fact]
        public void Produit_RowVersion_IsConfiguredAsConcurrencyToken()
        {
            using var context = CreateContext();

            var entityType = context.Model.FindEntityType(typeof(Produit));
            var property = entityType!.FindProperty(nameof(Produit.RowVersion));

            Assert.NotNull(property);
            Assert.True(property!.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        }

        [Fact]
        public void OrderDetail_RequiredRelationships_ToOrderHeaderAndProduit()
        {
            using var context = CreateContext();

            var entityType = context.Model.FindEntityType(typeof(OrderDetail));
            var foreignKeys = entityType!.GetForeignKeys().ToList();

            var toOrderHeader = foreignKeys.Single(fk => fk.PrincipalEntityType.ClrType == typeof(OrderHeader));
            var toProduit = foreignKeys.Single(fk => fk.PrincipalEntityType.ClrType == typeof(Produit));

            Assert.True(toOrderHeader.IsRequired);
            Assert.True(toProduit.IsRequired);
        }

        [Fact]
        public void ProcessedStripeEvent_StripeEventId_HasUniqueIndex()
        {
            using var context = CreateContext();

            var entityType = context.Model.FindEntityType(typeof(ProcessedStripeEvent));
            var index = entityType!.GetIndexes()
                .SingleOrDefault(i => i.Properties.Count == 1
                    && i.Properties[0].Name == nameof(ProcessedStripeEvent.StripeEventId));

            Assert.NotNull(index);
            Assert.True(index!.IsUnique);
        }

        [Fact]
        public void ProcessedStripeEvent_OrderId_IsOptional()
        {
            using var context = CreateContext();

            var entityType = context.Model.FindEntityType(typeof(ProcessedStripeEvent));
            var foreignKey = entityType!.GetForeignKeys().Single();

            Assert.False(foreignKey.IsRequired);
        }

        [Fact]
        public void IdentityTables_AreExcludedFromCosmechicsContextMigrations()
        {
            using var context = CreateContext();

            // "Exclu des migrations" n'est conservé que dans le modèle design-time
            // (le modèle "read-optimized" exposé par context.Model l'omet).
            var designTimeModel = context.GetService<IDesignTimeModel>().Model;

            var identityEntityTypes = new[]
            {
                typeof(AspNetRole), typeof(AspNetRoleClaim), typeof(AspNetUser),
                typeof(AspNetUserClaim), typeof(AspNetUserLogin), typeof(AspNetUserToken),
            };

            foreach (var clrType in identityEntityTypes)
            {
                var entityType = designTimeModel.FindEntityType(clrType);
                Assert.NotNull(entityType);
                Assert.True(entityType!.GetTableName() is not null);
                Assert.True(entityType.IsTableExcludedFromMigrations());
            }
        }
    }
}
