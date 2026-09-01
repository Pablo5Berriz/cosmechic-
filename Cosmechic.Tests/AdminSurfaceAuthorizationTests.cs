using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-SECURITY-002 (section 60/65) : Categories/Index est une vue de gestion
    // (liens Ajouter/Modifier/Supprimer) qui n'avait aucune restriction de rôle. Preuve de
    // régression que seul Admin y accède désormais.
    public class AdminSurfaceAuthorizationTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public AdminSurfaceAuthorizationTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        [Fact]
        public async Task CategoriesIndex_Anonymous_IsDenied()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/Categories/Index");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task CategoriesIndex_RegularCustomer_IsDenied()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync("/Categories/Index");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task CategoriesIndex_Admin_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.GetAsync("/Categories/Index");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        public void Dispose() => _factory.Dispose();
    }
}
