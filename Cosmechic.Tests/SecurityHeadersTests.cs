using System.Net.Http;
using System.Threading.Tasks;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-SECURITY-002 (section 57/65) : preuve que les en-têtes de sécurité HTTP
    // sont présents sur les réponses, avec une CSP script-src sans wildcard/'unsafe-eval'.
    public class SecurityHeadersTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        [Fact]
        public async Task HomePage_IncludesSecurityHeaders()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/");

            Assert.True(response.Headers.Contains("Content-Security-Policy"));
            Assert.True(response.Headers.Contains("X-Content-Type-Options"));
            Assert.True(response.Headers.Contains("X-Frame-Options"));
            Assert.True(response.Headers.Contains("Referrer-Policy"));

            var csp = string.Join(" ", response.Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("default-src 'self'", csp);
            Assert.DoesNotContain("unsafe-eval", csp);
            Assert.DoesNotContain("script-src *", csp);
            Assert.Contains("frame-ancestors 'none'", csp);

            Assert.Equal("nosniff", string.Join("", response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("DENY", string.Join("", response.Headers.GetValues("X-Frame-Options")));
        }

        [Fact]
        public async Task ErrorPage_AlsoIncludesSecurityHeaders()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/Home/Error");

            Assert.True(response.Headers.Contains("Content-Security-Policy"));
            Assert.True(response.Headers.Contains("X-Frame-Options"));
        }

        public void Dispose() => _factory.Dispose();
    }
}
