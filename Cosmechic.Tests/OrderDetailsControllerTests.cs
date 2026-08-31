using System.Net;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // Matrice de tests COSMECHIC-SECURITY-001, section 16 - OrderDetails.
    // Contrôleur entièrement réservé à Admin (moindre privilège, section 5) : même le
    // client propriétaire de la commande parente n'a pas d'accès direct.
    public class OrderDetailsControllerTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public OrderDetailsControllerTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        [Fact]
        public async Task Index_Anonymous_IsDenied()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/OrderDetails/Index");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Details_Anonymous_IsDenied()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync($"/OrderDetails/Details/{TestDataSeeder.OrderDetailAId}");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Create_Anonymous_IsDenied()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/OrderDetails/Create");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Delete_Anonymous_IsDenied()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.PostAsync($"/OrderDetails/Delete/{TestDataSeeder.OrderDetailAId}", content: null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Details_CustomerOwningParentOrder_IsStillDenied()
        {
            // OrderDetailAId appartient à la commande de Customer A : même le propriétaire
            // de la commande n'a pas de droit direct sur ses lignes via ce controller.
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/OrderDetails/Details/{TestDataSeeder.OrderDetailAId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Index_Admin_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.GetAsync("/OrderDetails/Index");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Details_Admin_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.GetAsync($"/OrderDetails/Details/{TestDataSeeder.OrderDetailAId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        public void Dispose() => _factory.Dispose();
    }
}
