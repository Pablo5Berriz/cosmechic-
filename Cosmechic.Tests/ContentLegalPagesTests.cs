using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Cosmechic.Controllers;
using Cosmechic.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-CONTENT-LEGAL-001 (section 30-31) : preuve que les pages institutionnelles/
    // légales ajoutées ou corrigées dans ce lot sont réellement publiques, que le footer ne
    // pointe vers aucun lien mort, qu'aucune route d'administration n'est exposée
    // anonymement, et que le formulaire Contact est protégé (CSRF + rate limiting).
    public class ContentLegalPagesTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        [Theory]
        [InlineData("/")]
        [InlineData("/Home/About")]
        [InlineData("/Home/Contact")]
        [InlineData("/Home/Faq")]
        [InlineData("/Home/Shipping")]
        [InlineData("/Home/Returns")]
        [InlineData("/Home/Privacy")]
        [InlineData("/Home/Terms")]
        public async Task EssentialPublicPages_AreAccessibleAnonymously(string route)
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Toutes les routes exposées dans le footer réécrit (_Layout.cshtml, section 18)
        // doivent résolvent en 200, qu'on soit connecté ou non — aucune ne doit être un lien
        // mort (l'ancien footer pointait vers /notrehistoire, /blog, "Catégorie 1/2/3", etc.
        // qui ne correspondaient à aucune route réelle).
        [Theory]
        [InlineData("/Home/About")]
        [InlineData("/Home/Contact")]
        [InlineData("/Categories/Customer")]
        [InlineData("/Home/Faq")]
        [InlineData("/Home/Shipping")]
        [InlineData("/Home/Returns")]
        [InlineData("/Home/Privacy")]
        [InlineData("/Home/Terms")]
        public async Task FooterLinks_ResolveSuccessfully(string route)
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // /Account/Index et /Account/Orders (également liés depuis le footer) exigent une
        // authentification : un visiteur anonyme n'obtient jamais 200 — ce n'est pas un lien
        // mort, juste une route protégée (TestAuthHandler répond 401 brut en environnement de
        // test plutôt que de rediriger, voir Infrastructure/TestAuthHandler.cs).
        [Theory]
        [InlineData("/Account/Index")]
        [InlineData("/Account/Orders")]
        public async Task FooterAccountLinks_RejectAnonymousUser(string route)
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // Aucune route d'administration (gestion des méthodes de livraison, taux de taxe,
        // marques) ne doit être accessible sans authentification, même après l'ajout des
        // pages publiques de ce lot.
        [Theory]
        [InlineData("/ShippingMethods")]
        [InlineData("/TaxRates")]
        [InlineData("/Brands")]
        public async Task AdminOnlyRoutes_AreNotExposedAnonymously(string route)
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync(route);

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Checkout_Summary_ContainsPolicyLinks()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync("/Cart/Summary");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("/Home/Shipping", html);
            Assert.Contains("/Home/Returns", html);
            Assert.Contains("/Home/Terms", html);
            Assert.Contains("/Home/Privacy", html);
        }

        [Fact]
        public async Task ContactPost_ValidInput_SendsEmailAndRedirects()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.PostAsync("/Home/Contact", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Name"] = "Client Test",
                ["Email"] = "client@example.com",
                ["Message"] = "Bonjour, ceci est un test.",
            }));

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var email = _factory.EmailSender.SentEmails.Single();
            Assert.Equal("equipe.cosmechic@gmail.com", email.Recipient);
            Assert.Contains("Client Test", email.HtmlBody);
        }

        [Fact]
        public async Task ContactPost_InvalidInput_ReturnsViewWithoutSendingEmail()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.PostAsync("/Home/Contact", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Name"] = "",
                ["Email"] = "not-an-email",
                ["Message"] = "",
            }));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(_factory.EmailSender.SentEmails);
        }

        // Comportement déterministe si SMTP est indisponible (section 9) : aucune 500,
        // message générique, pas de fuite de détail d'exception au client.
        [Fact]
        public async Task ContactPost_EmailSenderThrows_ReturnsGenericErrorWithoutCrashing()
        {
            var client = _factory.CreateTestClient().AsAnonymous();
            _factory.EmailSender.ExceptionToThrow = new InvalidOperationException("Smtp:Host non configuré.");

            var response = await client.PostAsync("/Home/Contact", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Name"] = "Client Test",
                ["Email"] = "client@example.com",
                ["Message"] = "Bonjour",
            }));

            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("Smtp:Host", html);
        }

        // Preuve structurelle que l'action POST Contact est protégée par le jeton
        // antiforgery : le double de test (NoOpAntiforgery) neutralise volontairement la
        // validation runtime pour permettre aux tests HTTP de poster sans jeton (comme pour
        // toutes les autres actions [ValidateAntiForgeryToken] du projet) — la preuve que la
        // protection existe réellement se fait donc par réflexion sur l'attribut, pas par un
        // rejet HTTP observable dans ce harnais.
        [Fact]
        public void ContactPost_Action_IsProtectedByAntiforgeryAndRateLimiting()
        {
            var method = typeof(HomeController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(m => m.Name == nameof(HomeController.Contact)
                    && m.GetParameters().Length == 1
                    && m.GetCustomAttribute<HttpPostAttribute>() != null);

            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
            Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>());
        }

        [Fact]
        public async Task ContactForm_ExceedingRateLimitPolicy_Returns429()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            HttpResponseMessage? last = null;
            for (var i = 0; i < 10; i++)
            {
                last = await client.PostAsync("/Home/Contact", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Name"] = "Client Test",
                    ["Email"] = "client@example.com",
                    ["Message"] = "Bonjour",
                }));
                if (last.StatusCode == (HttpStatusCode)429)
                {
                    break;
                }
            }

            Assert.Equal((HttpStatusCode)429, last!.StatusCode);
        }

        // La page Retours n'affiche jamais un délai/une politique fabriqués : tant que
        // CommercePolicy:ReturnWindowDays n'est pas configuré, le texte doit rester le
        // fallback explicite "non encore défini" plutôt qu'un nombre inventé.
        [Fact]
        // COSMECHIC-BUSINESS-POLICY-001 (section 3) : RETURN_WINDOW_DAYS=30 est désormais
        // une décision PM approuvée et configurée (appsettings.json), plus une valeur
        // absente — la page affiche donc la vraie politique plutôt que le repli
        // "non défini". Le repli lui-même (aucune valeur fabriquée quand la config est
        // absente) reste couvert au niveau service par ReturnServiceTests/ReturnWindowTests
        // (ReturnWindowDays=null y est passé explicitement).
        public async Task ReturnsPage_WithConfiguredPolicy_ShowsRealApprovedValue_NotFallback()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/Home/Returns");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Contains("30 jours", html);
            Assert.DoesNotContain("n'a pas encore été défini par", html);
        }

        // La page Conditions ne doit jamais affirmer une juridiction non validée.
        [Fact]
        public async Task TermsPage_NeverFabricatesJurisdiction()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/Home/Terms");
            var html = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain("loi canadienne", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("n'ont pas encore été déterminés par", html);
        }

        // Chaque nouvelle page publique fournit un titre distinct et une meta description
        // (section 20/26) — vérifié sur un échantillon représentatif.
        [Theory]
        [InlineData("/Home/Faq", "Foire aux questions")]
        [InlineData("/Home/Shipping", "Livraison")]
        [InlineData("/Home/Returns", "Retours et remboursements")]
        public async Task NewPages_HaveTitleAndMetaDescription(string route, string expectedTitleFragment)
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync(route);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Contains(expectedTitleFragment, html);
            Assert.Contains("name=\"description\"", html);
        }

        public void Dispose() => _factory.Dispose();
    }
}
