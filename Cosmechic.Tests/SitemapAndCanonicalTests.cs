using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-BUSINESS-POLICY-001 (section 9B) : PRODUCTION_DOMAIN approuvé par le PM
    // (https://cosmechic.ca). Empêche explicitement les régressions les plus probables :
    // localhost/le Host de la requête de test qui fuiterait dans une URL absolue,
    // http:// non sécurisé, ou www. utilisé comme origine canonique.
    public class SitemapAndCanonicalTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        [Fact]
        public async Task Sitemap_IsServedAtConventionalPath_WithCorrectContentType()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/sitemap.xml");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.StartsWith("application/xml", response.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task Sitemap_UsesConfiguredProductionDomain_NeverLocalhostOrHttp()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var body = await (await client.GetAsync("/sitemap.xml")).Content.ReadAsStringAsync();

            Assert.Contains("https://cosmechic.ca", body);
            Assert.DoesNotContain("localhost", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("127.0.0.1", body);
            // Le namespace XML du protocole sitemaps.org est fixé à "http://..." par la
            // spécification (jamais une URL réelle) — seules les valeurs <loc> doivent être
            // vérifiées, pas le document entier.
            Assert.DoesNotContain("<loc>http://", body);
        }

        [Fact]
        public async Task Sitemap_NeverUsesWwwAsCanonicalOrigin()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var body = await (await client.GetAsync("/sitemap.xml")).Content.ReadAsStringAsync();

            Assert.DoesNotContain("www.cosmechic.ca", body);
        }

        [Theory]
        [InlineData("/Account/")]
        [InlineData("/Identity/")]
        [InlineData("/Cart/")]
        [InlineData("/OrderHeaders/")]
        [InlineData("/OrderOperations/")]
        [InlineData("/webhooks/")]
        public async Task Sitemap_NeverListsPrivateOrAdminRoutes(string forbiddenFragment)
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var body = await (await client.GetAsync("/sitemap.xml")).Content.ReadAsStringAsync();

            Assert.DoesNotContain(forbiddenFragment, body);
        }

        [Fact]
        public async Task Canonical_UsesConfiguredProductionDomain_NeverLocalhostOrHttp()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var html = await (await client.GetAsync("/Home/Terms")).Content.ReadAsStringAsync();

            Assert.Contains("<link rel=\"canonical\" href=\"https://cosmechic.ca/Home/Terms\" />", html);
            Assert.DoesNotContain("rel=\"canonical\" href=\"http://", html);
            Assert.DoesNotContain("rel=\"canonical\" href=\"https://localhost", html);
            Assert.DoesNotContain("rel=\"canonical\" href=\"https://www.cosmechic.ca", html);
        }

        [Fact]
        public async Task WwwHost_RedirectsPermanently_ToApexDomain()
        {
            var client = _factory.CreateTestClient().AsAnonymous();
            client.DefaultRequestHeaders.Host = "www.cosmechic.ca";

            var response = await client.GetAsync("/Home/Terms");

            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            Assert.Equal("https://cosmechic.ca/Home/Terms", response.Headers.Location?.ToString());
        }

        [Fact]
        public async Task NonWwwHost_IsNotRedirected()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/Home/Terms");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        public void Dispose() => _factory.Dispose();
    }
}
