using System.Net.Http;
using System.Threading.Tasks;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-QA-RELEASE-001 (section 19) : recertification de robots.txt — le
    // endpoint webhook (POST /webhooks/stripe) n'y figurait pas, contrairement aux
    // autres surfaces techniques/privées déjà listées.
    public class RobotsTxtTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        [Fact]
        public async Task RobotsTxt_DisallowsPrivateAndTechnicalSurfaces()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/robots.txt");
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode);
            foreach (var path in new[] { "/Account/", "/Identity/", "/Cart/", "/OrderHeaders/", "/Returns/", "/ShippingMethods/", "/TaxRates/", "/Brands/", "/webhooks/" })
            {
                Assert.Contains($"Disallow: {path}", body);
            }
        }

        public void Dispose() => _factory.Dispose();
    }
}
