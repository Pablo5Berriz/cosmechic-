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

        // COSMECHIC-COMMERCE-OPERATIONS-001B-CLOSURE-1 : recertification du correctif
        // overposting d'OrderHeadersController.Edit (audit 001B, section 9). Un POST admin
        // qui tente d'injecter OrderTotal/OrderStatus/PaymentStatus/ApplicationUserId doit
        // être silencieusement ignoré sur ces champs — seuls Name/PhoneNumber/StreetAddress/
        // City/State/PostalCode sont réellement mutables par ce formulaire.
        [Fact]
        public async Task Edit_AdminPost_CannotTamperWithFinancialSnapshotOrStatus()
        {
            var before = _factory.Query(ctx => ctx.OrderHeaders.Single(o => o.Id == TestDataSeeder.OrderHeaderAId));
            var originalOrderTotal = before.OrderTotal;
            var originalOrderStatus = before.OrderStatus;
            var originalPaymentStatus = before.PaymentStatus;
            var originalApplicationUserId = before.ApplicationUserId;

            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.PostAsync(
                $"/OrderHeaders/Edit/{TestDataSeeder.OrderHeaderAId}",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Id"] = TestDataSeeder.OrderHeaderAId.ToString(),
                    ["Name"] = "Nom Modifie",
                    ["PhoneNumber"] = "5145550000",
                    ["StreetAddress"] = "999 rue Modifiee",
                    ["City"] = "Laval",
                    ["State"] = "QC",
                    ["PostalCode"] = "H1H1H1",
                    // Tentative d'overposting : ces champs ne font pas partie du [Bind]
                    // de OrderHeadersController.Edit et doivent être intégralement ignorés.
                    ["OrderTotal"] = "999999.99",
                    ["OrderStatus"] = "Completed",
                    ["PaymentStatus"] = "Refunded",
                    ["ApplicationUserId"] = TestIdentities.CustomerBId,
                }));

            // Redirection = succès applicatif (les champs autorisés sont valides).
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);

            var after = _factory.Query(ctx => ctx.OrderHeaders.Single(o => o.Id == TestDataSeeder.OrderHeaderAId));

            // Snapshot financier et statuts : totalement inchangés malgré la tentative.
            Assert.Equal(originalOrderTotal, after.OrderTotal);
            Assert.Equal(originalOrderStatus, after.OrderStatus);
            Assert.Equal(originalPaymentStatus, after.PaymentStatus);
            Assert.Equal(originalApplicationUserId, after.ApplicationUserId);

            // Champs légitimement adressés par ce formulaire : bien appliqués.
            Assert.Equal("Nom Modifie", after.Name);
            Assert.Equal("999 rue Modifiee", after.StreetAddress);
        }

        public void Dispose() => _factory.Dispose();
    }
}
