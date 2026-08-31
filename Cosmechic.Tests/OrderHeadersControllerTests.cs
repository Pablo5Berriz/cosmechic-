using System.Net;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // Matrice de tests COSMECHIC-SECURITY-001, section 16 - OrderHeaders.
    public class OrderHeadersControllerTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public OrderHeadersControllerTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        [Fact]
        public async Task Details_Anonymous_IsDenied()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync($"/OrderHeaders/Details/{TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Details_OwnOrder_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/OrderHeaders/Details/{TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Details_OtherCustomersOrder_IsDenied()
        {
            var client = _factory.CreateTestClient().AsCustomerB();

            // La commande OrderHeaderAId appartient à Customer A.
            var response = await client.GetAsync($"/OrderHeaders/Details/{TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Details_Admin_CanViewAnyOrder()
        {
            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.GetAsync($"/OrderHeaders/Details/{TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Create_Customer_IsDenied()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync("/OrderHeaders/Create");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Create_Admin_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.GetAsync("/OrderHeaders/Create");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Edit_Customer_IsDenied_EvenOnOwnOrder()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            // CRUD scaffold administratif : même sur sa propre commande, un client ne peut
            // pas changer librement OrderStatus/PaymentStatus/OrderTotal (section 6).
            var response = await client.GetAsync($"/OrderHeaders/Edit/{TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Delete_Customer_IsDenied()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/OrderHeaders/Delete/{TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Index_Customer_OnlySeesOwnOrders()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync("/OrderHeaders/Index");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // Garde non-régression sur le filtrage déjà correct de Index() : le nom du
            // client B (marqueur peu ambigu) ne doit jamais apparaître dans la liste de A.
            Assert.DoesNotContain("customer-b", body);
        }

        public void Dispose() => _factory.Dispose();
    }
}
