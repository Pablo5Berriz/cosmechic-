using Cosmechic.Controllers;
using Cosmechic.Models;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Xunit.Abstractions;

namespace Cosmechic.Tests
{
    // COSMECHIC-CATALOG-001 (section 52) : la recherche utilise EF.Functions.Collate,
    // une fonction relationnelle qu'EF Core InMemory ne peut pas traduire — ces tests
    // doivent donc s'exécuter contre un vrai SQL Server (SqlServerFixture, DATA-001).
    [Collection("SqlServerFixture collection")]
    public class CatalogSearchSqlServerTests
    {
        private readonly SqlServerFixture _fixture;
        private readonly ITestOutputHelper _output;

        public CatalogSearchSqlServerTests(SqlServerFixture fixture, ITestOutputHelper output)
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

        private static async Task<(Category category, Brand brand)> SeedBaseAsync(CosmechicsContext context)
        {
            var category = new Category { Nom = $"Categorie-{Guid.NewGuid():N}", Image = "c.jpg", Disponible = true };
            context.Categories.Add(category);
            var brand = new Brand { Nom = $"Marque-{Guid.NewGuid():N}", Slug = $"marque-{Guid.NewGuid():N}", Disponible = true };
            context.Brands.Add(brand);
            await context.SaveChangesAsync();
            return (category, brand);
        }

        private static Produit MakeProduit(Category category, Brand? brand, string nom, decimal prix, decimal stock, bool disponible = true, DateTime? dateCreation = null)
            => new()
            {
                Nom = nom,
                CategorieId = category.CategorieId,
                Prix = prix,
                Stock = stock,
                Disponible = disponible,
                Image = "p.jpg",
                Sku = $"SKU-{Guid.NewGuid():N}"[..12],
                Slug = $"slug-{Guid.NewGuid():N}",
                BrandId = brand?.BrandId,
                DateCreation = dateCreation ?? DateTime.UtcNow,
            };

        [Fact]
        public async Task Rechercher_AccentInsensitivePartialName_FindsMatch()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var (category, brand) = await SeedBaseAsync(context);
            context.Produits.Add(MakeProduit(category, brand, "Beurre de Karité Pur", 10m, 5));
            await context.SaveChangesAsync();

            var controller = new ProduitsController(context, null!, null!);
            var result = await controller.Rechercher(new CatalogSearchViewModel { Q = "karite" });

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<CatalogSearchViewModel>(view.Model);
            Assert.Equal(1, model.TotalResults);
            Assert.Contains(model.Products, p => p.Nom == "Beurre de Karité Pur");
        }

        [Fact]
        public async Task Rechercher_NoMatch_ReturnsZeroResultsWithoutThrowing()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            await SeedBaseAsync(context);

            var controller = new ProduitsController(context, null!, null!);
            var result = await controller.Rechercher(new CatalogSearchViewModel { Q = "produit-inexistant-xyz" });

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<CatalogSearchViewModel>(view.Model);
            Assert.Equal(0, model.TotalResults);
            Assert.Empty(model.Products);
        }

        [Fact]
        public async Task Rechercher_CategoryFilter_OnlyReturnsMatchingCategory()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var (categoryA, brand) = await SeedBaseAsync(context);
            var categoryB = new Category { Nom = $"Categorie-{Guid.NewGuid():N}", Image = "c.jpg", Disponible = true };
            context.Categories.Add(categoryB);
            await context.SaveChangesAsync();

            context.Produits.Add(MakeProduit(categoryA, brand, "Produit A", 10m, 5));
            context.Produits.Add(MakeProduit(categoryB, brand, "Produit B", 10m, 5));
            await context.SaveChangesAsync();

            var controller = new ProduitsController(context, null!, null!);
            var result = await controller.Rechercher(new CatalogSearchViewModel { CategoryId = categoryA.CategorieId });

            var model = Assert.IsType<CatalogSearchViewModel>(((ViewResult)result).Model);
            Assert.Equal(1, model.TotalResults);
            Assert.Equal("Produit A", model.Products[0].Nom);
        }

        [Fact]
        public async Task Rechercher_PriceRangeFilter_ExcludesOutOfRange()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var (category, brand) = await SeedBaseAsync(context);
            context.Produits.Add(MakeProduit(category, brand, "Pas cher", 5m, 5));
            context.Produits.Add(MakeProduit(category, brand, "Prix moyen", 20m, 5));
            context.Produits.Add(MakeProduit(category, brand, "Cher", 100m, 5));
            await context.SaveChangesAsync();

            var controller = new ProduitsController(context, null!, null!);
            var result = await controller.Rechercher(new CatalogSearchViewModel { CategoryId = category.CategorieId, MinPrice = 10m, MaxPrice = 30m });

            var model = Assert.IsType<CatalogSearchViewModel>(((ViewResult)result).Model);
            Assert.Equal(1, model.TotalResults);
            Assert.Equal("Prix moyen", model.Products[0].Nom);
        }

        [Fact]
        public async Task Rechercher_AvailableOnly_ExcludesOutOfStockAndDeactivated()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var (category, brand) = await SeedBaseAsync(context);
            context.Produits.Add(MakeProduit(category, brand, "En stock", 10m, 5, disponible: true));
            context.Produits.Add(MakeProduit(category, brand, "Rupture", 10m, 0, disponible: true));
            context.Produits.Add(MakeProduit(category, brand, "Desactive", 10m, 5, disponible: false));
            await context.SaveChangesAsync();

            var controller = new ProduitsController(context, null!, null!);
            var result = await controller.Rechercher(new CatalogSearchViewModel { CategoryId = category.CategorieId, AvailableOnly = true });

            var model = Assert.IsType<CatalogSearchViewModel>(((ViewResult)result).Model);
            Assert.Equal(1, model.TotalResults);
            Assert.Equal("En stock", model.Products[0].Nom);
        }

        [Fact]
        public async Task Rechercher_SortPriceAscAndDesc_OrdersCorrectly()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var (category, brand) = await SeedBaseAsync(context);
            context.Produits.Add(MakeProduit(category, brand, "Milieu", 20m, 5));
            context.Produits.Add(MakeProduit(category, brand, "Bas", 5m, 5));
            context.Produits.Add(MakeProduit(category, brand, "Haut", 50m, 5));
            await context.SaveChangesAsync();

            var controller = new ProduitsController(context, null!, null!);

            var asc = Assert.IsType<CatalogSearchViewModel>(((ViewResult)await controller.Rechercher(new CatalogSearchViewModel { CategoryId = category.CategorieId, Sort = "price_asc" })).Model);
            Assert.Equal(new[] { "Bas", "Milieu", "Haut" }, asc.Products.Select(p => p.Nom));

            var desc = Assert.IsType<CatalogSearchViewModel>(((ViewResult)await controller.Rechercher(new CatalogSearchViewModel { CategoryId = category.CategorieId, Sort = "price_desc" })).Model);
            Assert.Equal(new[] { "Haut", "Milieu", "Bas" }, desc.Products.Select(p => p.Nom));
        }

        [Fact]
        public async Task Rechercher_Newest_OrdersByDateCreationDescending()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var (category, brand) = await SeedBaseAsync(context);
            var now = DateTime.UtcNow;
            context.Produits.Add(MakeProduit(category, brand, "Ancien", 10m, 5, dateCreation: now.AddDays(-10)));
            context.Produits.Add(MakeProduit(category, brand, "Recent", 10m, 5, dateCreation: now));
            await context.SaveChangesAsync();

            var controller = new ProduitsController(context, null!, null!);
            var result = await controller.Rechercher(new CatalogSearchViewModel { CategoryId = category.CategorieId, Sort = "newest" });

            var model = Assert.IsType<CatalogSearchViewModel>(((ViewResult)result).Model);
            Assert.Equal("Recent", model.Products[0].Nom);
        }

        [Fact]
        public async Task Rechercher_Pagination_SplitsResultsAcrossPages()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var (category, brand) = await SeedBaseAsync(context);
            for (var i = 0; i < 5; i++)
            {
                context.Produits.Add(MakeProduit(category, brand, $"Produit-{i:D2}", 10m, 5));
            }
            await context.SaveChangesAsync();

            var controller = new ProduitsController(context, null!, null!);
            var page1 = Assert.IsType<CatalogSearchViewModel>(((ViewResult)await controller.Rechercher(new CatalogSearchViewModel { CategoryId = category.CategorieId, Sort = "name_asc", Page = 1, PageSize = 2 })).Model);
            var page2 = Assert.IsType<CatalogSearchViewModel>(((ViewResult)await controller.Rechercher(new CatalogSearchViewModel { CategoryId = category.CategorieId, Sort = "name_asc", Page = 2, PageSize = 2 })).Model);

            Assert.Equal(5, page1.TotalResults);
            Assert.Equal(3, page1.TotalPages);
            Assert.Equal(2, page1.Products.Count);
            Assert.Equal(2, page2.Products.Count);
            Assert.NotEqual(page1.Products[0].ProduitId, page2.Products[0].ProduitId);
        }

        [Fact]
        public async Task Rechercher_InvalidPageAndPageSize_AreNormalized()
        {
            if (SkipIfUnavailable()) return;

            using var context = _fixture.CreateBusinessContext();
            var (category, brand) = await SeedBaseAsync(context);
            context.Produits.Add(MakeProduit(category, brand, "Produit", 10m, 5));
            await context.SaveChangesAsync();

            var controller = new ProduitsController(context, null!, null!);
            var result = await controller.Rechercher(new CatalogSearchViewModel { CategoryId = category.CategorieId, Page = -5, PageSize = 999999 });

            var model = Assert.IsType<CatalogSearchViewModel>(((ViewResult)result).Model);
            Assert.Equal(1, model.Page);
            Assert.True(model.PageSize <= CatalogSearchViewModel.MaxPageSize);
        }
    }
}
