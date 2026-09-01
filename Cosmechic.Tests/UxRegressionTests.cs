using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-UX-001 (section 23) : preuve que la refonte UI/UX n'a introduit aucune
    // régression structurelle — navigation, catalogue, checkout, compte, Identity,
    // admin, contenu et accessibilité de base. Ne re-teste pas ce que les suites des
    // lots précédents couvrent déjà (montants, ownership, CSRF, rate limiting...), qui
    // restent vérifiés par la régression complète (314 tests historiques).
    public class UxRegressionTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public UxRegressionTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        // ---- NAVIGATION ----

        [Fact]
        public async Task Layout_ContainsSkipLinkAndResponsiveNavToggle()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

            Assert.Contains("cx-skip-link", html);
            Assert.Contains("navbar-toggler", html);
            Assert.Contains("data-bs-target=\"#navbarSupportedContent\"", html);
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/Home/About")]
        [InlineData("/Home/Contact")]
        [InlineData("/Home/Faq")]
        public async Task MainNavLinks_ResolveAnonymously(string route)
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AccountNav_OnlyRendersForAuthenticatedUser()
        {
            var anonymousHtml = await (await _factory.CreateTestClient().AsAnonymous().GetAsync("/")).Content.ReadAsStringAsync();
            var authenticatedHtml = await (await _factory.CreateTestClient().AsCustomerA().GetAsync("/Account/Index")).Content.ReadAsStringAsync();

            Assert.DoesNotContain("Navigation du compte", anonymousHtml);
            Assert.Contains("Navigation du compte", authenticatedHtml);
        }

        // Note : /Produits/Index n'est volontairement PAS dans cette liste — ce n'est pas
        // une route [Authorize(Roles="Admin")] (héritage CATALOG-001) : elle sert un rendu
        // client (Ajouter au panier) ou admin (CRUD) selon le rôle courant sur la même URL.
        // Corriger cette architecture est hors périmètre UX-001 (modifierait les règles
        // d'autorisation) — documenté dans l'audit comme piste pour un lot futur.
        [Theory]
        [InlineData("/Brands/Index")]
        [InlineData("/ShippingMethods/Index")]
        [InlineData("/TaxRates/Index")]
        public async Task NewAdminSidebarLinks_RejectAnonymousAccess(string route)
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync(route);

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData("/Brands/Index")]
        [InlineData("/ShippingMethods/Index")]
        [InlineData("/TaxRates/Index")]
        public async Task NewAdminSidebarLinks_RejectNonAdminCustomer(string route)
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync(route);

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ProduitsIndex_ServesCustomerViewToAnonymous_AdminViewToAdmin()
        {
            // Comportement réel (CATALOG-001, non modifié par ce lot) : même route, rendu
            // différent selon le rôle, sans blocage d'autorisation.
            var anonymousHtml = await (await _factory.CreateTestClient().AsAnonymous().GetAsync("/Produits/Index")).Content.ReadAsStringAsync();
            var adminHtml = await (await _factory.CreateTestClient().AsAdmin().GetAsync("/Produits/Index")).Content.ReadAsStringAsync();

            Assert.Contains("Ajouter au panier", anonymousHtml);
            Assert.DoesNotContain("Ajouter au panier", adminHtml);
            Assert.Contains("Modifier", adminHtml);
        }

        [Theory]
        [InlineData("/Produits/Index")]
        [InlineData("/Brands/Index")]
        [InlineData("/ShippingMethods/Index")]
        [InlineData("/TaxRates/Index")]
        [InlineData("/Categories/Index")]
        [InlineData("/OrderHeaders/Index")]
        public async Task NewAdminSidebarLinks_ResolveForAdmin(string route)
        {
            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // ---- CATALOGUE ----

        [Fact]
        public async Task SearchPage_RendersSearchFormAndFilters()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var html = await (await client.GetAsync("/Produits/Rechercher")).Content.ReadAsStringAsync();

            Assert.Contains("name=\"Q\"", html);
            Assert.Contains("name=\"CategoryId\"", html);
            Assert.Contains("name=\"AvailableOnly\"", html);
        }

        [Fact]
        public async Task SearchPage_NoResults_ShowsEmptyStateNotBlankPage()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            // COSMECHIC-CATALOG-001 : le filtre texte (Q) s'appuie sur EF.Functions.Collate,
            // non supporté par le fournisseur InMemory utilisé par ce harnais de test (il
            // fonctionne réellement contre SQL Server, voir CatalogSearchSqlServerTests) —
            // on déclenche donc l'état "aucun résultat" via un filtre catégorie inexistant,
            // qui n'emprunte pas ce chemin de collation.
            var html = await (await client.GetAsync($"/Produits/Rechercher?CategoryId={TestDataSeeder.NonExistentId}")).Content.ReadAsStringAsync();

            Assert.Contains("Aucun produit trouvé", html);
        }

        [Fact]
        public async Task CategoryProductListing_RendersRealProductAndNoBrokenPreviousBug()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync($"/Produits/Customer?id={TestDataSeeder.CategoryId}");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Creme hydratante", html);
        }

        [Fact]
        public async Task ProductDetailPage_TitleReflectsProductName_NotGenericLabel()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var html = await (await client.GetAsync($"/Produits/ItemDetails?productId={TestDataSeeder.ProduitId}")).Content.ReadAsStringAsync();

            // COSMECHIC-UX-001 : régression du titre générique "Ajout produit" identique
            // sur toutes les fiches produit (section 21 : titres descriptifs par page).
            Assert.Contains("<title>Creme hydratante", html);
        }

        [Fact]
        public async Task CartIndex_DiscoverMoreProductsLink_PointsToRealRoute()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var html = await (await client.GetAsync("/Cart/Index")).Content.ReadAsStringAsync();

            // COSMECHIC-UX-001 : "Continuer les achats" pointait vers un contrôleur
            // "Categorie" (singulier) inexistant — lien mort.
            Assert.DoesNotContain("asp-controller=\"Categorie\"", html);
            Assert.Contains("/Categories/Customer", html);
        }

        // ---- COMPTE ----

        [Theory]
        [InlineData("/Account/Index")]
        [InlineData("/Account/Profile")]
        [InlineData("/Account/Addresses")]
        [InlineData("/Account/Orders")]
        [InlineData("/Account/Returns")]
        public async Task AccountPages_RenderForOwner(string route)
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AccountReturns_EmptyState_RendersWithoutError()
        {
            // CustomerA n'a aucun retour dans la fixture standard : la page doit rendre un
            // état vide clair plutôt qu'une erreur ou une grille cassée.
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync("/Account/Returns");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // ---- IDENTITY ----

        [Theory]
        [InlineData("/Identity/Account/Login")]
        [InlineData("/Identity/Account/Register")]
        [InlineData("/Identity/Account/ForgotPassword")]
        public async Task IdentityPages_StillRespondAfterVisualUpdates(string route)
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // ---- CONTENU / ACCESSIBILITÉ STRUCTURELLE ----

        [Theory]
        [InlineData("/")]
        [InlineData("/Home/About")]
        [InlineData("/Produits/Rechercher")]
        public async Task Pages_HaveExactlyOneH1(string route)
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var html = await (await client.GetAsync(route)).Content.ReadAsStringAsync();

            var count = System.Text.RegularExpressions.Regex.Matches(html, "<h1[ >]").Count;
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task CategoriesCustomer_AvailabilityBadge_ReflectsActualState()
        {
            // COSMECHIC-UX-001 : le badge de disponibilité affichait toujours bg-success,
            // y compris pour une catégorie non disponible. On seed une catégorie non
            // disponible et on vérifie que le badge n'est jamais vert pour elle.
            // Nom sans accent : les caractères Latin-1 étendu (ex. "é") sont encodés par
            // défaut en entités numériques HTML par Razor (rendu identique dans le
            // navigateur, mais non comparable par égalité de sous-chaîne brute ici).
            _factory.Seed(context =>
            {
                context.Categories.Add(new Cosmechic.Models.Category
                {
                    CategorieId = 999,
                    Nom = "Categorie Fermee Test",
                    Image = "x.jpg",
                    Disponible = false,
                });
            });
            var client = _factory.CreateTestClient().AsAnonymous();

            var html = await (await client.GetAsync("/Categories/Customer?pageSize=50")).Content.ReadAsStringAsync();

            Assert.Contains("Categorie Fermee Test", html);
            // Le badge de disponibilité est rendu juste après le nom dans le gabarit
            // (h2 puis span.badge) — fenêtre de recherche en avant, pas en arrière.
            var closedCardIndex = html.IndexOf("Categorie Fermee Test", StringComparison.Ordinal);
            var badgeWindow = html.Substring(closedCardIndex, Math.Min(400, html.Length - closedCardIndex));
            Assert.Contains("bg-secondary", badgeWindow);
        }

        public void Dispose() => _factory.Dispose();
    }
}
