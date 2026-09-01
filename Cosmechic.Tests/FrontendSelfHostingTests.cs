using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-RELEASE-CONFIG-001 (section 6) : jQuery et le bundle JS Bootstrap sont
    // désormais servis depuis wwwroot/lib (déjà présents dans le dépôt) plutôt que depuis
    // un CDN tiers. COSMECHIC-QA-RELEASE-001 avait constaté que le bac à sable ne pouvait
    // pas joindre code.jquery.com/cdn.jsdelivr.net, rendant la navigation mobile
    // (.navbar-toggler, menus déroulants) non vérifiable — ces deux scripts pilotent
    // entièrement cette interactivité. Ce test prouve que la page ne dépend plus d'un CDN
    // pour ces deux fichiers, et que les fichiers locaux référencés sont réellement servis.
    public class FrontendSelfHostingTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        [Fact]
        public async Task Layout_ReferencesLocalJQueryAndBootstrapBundle_NotCdn()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

            Assert.Contains("/lib/jquery/dist/jquery.min.js", html);
            Assert.Contains("/lib/bootstrap/dist/js/bootstrap.bundle.min.js", html);
            Assert.DoesNotContain("code.jquery.com", html);
            Assert.DoesNotContain("cdn.jsdelivr.net/npm/bootstrap@", html);
        }

        [Theory]
        [InlineData("/lib/jquery/dist/jquery.min.js")]
        [InlineData("/lib/bootstrap/dist/js/bootstrap.bundle.min.js")]
        public async Task SelfHostedScript_IsServedSuccessfully(string path)
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(body.Length > 1000, "Le fichier local attendu semble vide ou tronqué.");
        }

        public void Dispose() => _factory.Dispose();
    }
}
