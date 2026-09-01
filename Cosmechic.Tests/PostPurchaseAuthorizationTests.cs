using System.Net;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 76/77) : autorisation du reçu (ownership)
    // et de la surface admin post-achat (un client ne peut jamais approuver un retour,
    // rembourser, marquer expédiée/livrée, ou décider d'une remise en stock).
    public class PostPurchaseAuthorizationTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public PostPurchaseAuthorizationTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        [Fact]
        public async Task Receipt_Owner_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/Cart/Receipt/{TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Receipt_ForeignOrder_IsDenied()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/Cart/Receipt/{TestDataSeeder.OrderHeaderBId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Receipt_Admin_IsAllowedOnAnyOrder()
        {
            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.GetAsync($"/Cart/Receipt/{TestDataSeeder.OrderHeaderBId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Receipt_Anonymous_IsDenied()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync($"/Cart/Receipt/{TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Customer_CannotAccessOrderOperationsDetails()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/OrderOperations/Details/{TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Customer_CannotMarkShipped()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync(
                "/OrderOperations/MarkShipped",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["OrderId"] = TestDataSeeder.OrderHeaderAId.ToString() }));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Customer_CannotMarkDelivered()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync(
                "/OrderOperations/MarkDelivered",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["orderId"] = TestDataSeeder.OrderHeaderAId.ToString() }));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Customer_CannotTriggerRefund()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync(
                "/OrderOperations/TriggerRefund",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["OrderId"] = TestDataSeeder.OrderHeaderAId.ToString(),
                    ["Amount"] = "25.00",
                }));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Customer_CannotApproveReturn()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync(
                "/OrderOperations/ApproveReturn",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["ReturnRequestId"] = "1" }));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Customer_CannotCompleteRestock()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync(
                "/OrderOperations/CompleteRestock",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["ReturnItemId"] = "1" }));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Admin_CanAccessOrderOperationsDetails()
        {
            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.GetAsync($"/OrderOperations/Details/{TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ForeignOrder_CustomerCannotRequestReturnOnIt()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/Returns/Request?orderId={TestDataSeeder.OrderHeaderBId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Customer_CannotCancelForeignOrder()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync(
                "/Cart/CancelOrder",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["orderId"] = TestDataSeeder.OrderHeaderBId.ToString() }));

            // CancelOrder redirige toujours vers OrderHeaders/Details (302) même en cas de
            // refus applicatif (TempData["error"]) : la commande de B ne doit dans tous les
            // cas jamais être mutée.
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            var status = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == TestDataSeeder.OrderHeaderBId).OrderStatus);
            Assert.NotEqual("Cancelled", status);
        }

        public void Dispose() => _factory.Dispose();
    }
}
