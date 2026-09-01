using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-SECURITY-002 (section 8/65) : la policy "AuthSensitive" doit effectivement
    // rejeter (429) un client qui dépasse la limite sur une route d'authentification
    // sensible, sans affecter les routes hors périmètre (ex. le webhook Stripe).
    public class RateLimitingTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        [Fact]
        public async Task Login_ExceedingPermitLimit_Returns429()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            HttpResponseMessage? last = null;
            for (var i = 0; i < 15; i++)
            {
                last = await client.GetAsync("/Identity/Account/Login");
                if (last.StatusCode == (HttpStatusCode)429)
                {
                    break;
                }
            }

            Assert.Equal((HttpStatusCode)429, last!.StatusCode);
        }

        [Fact]
        public async Task NonAuthRoute_IsNotAffectedByAuthSensitivePolicy()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            for (var i = 0; i < 15; i++)
            {
                var response = await client.GetAsync("/");
                Assert.NotEqual((HttpStatusCode)429, response.StatusCode);
            }
        }

        public void Dispose() => _factory.Dispose();
    }
}
