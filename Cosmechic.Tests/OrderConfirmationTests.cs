using System.Net;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // Matrice de tests COSMECHIC-SECURITY-001, section 16/17 - CartController.OrderConfirmation
    // (P0). Preuve automatisée que Stripe SessionService.Get n'est jamais appelé pour une
    // commande dont l'appelant n'est pas propriétaire.
    public class OrderConfirmationTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public OrderConfirmationTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        [Fact]
        public async Task OwnOrder_ReachesAuthorizedFlow_AndCallsStripeExactlyOnce()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/Cart/OrderConfirmation?id={TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, _factory.PaymentSessionService.CallCount);
            Assert.Equal("cs_test_ordera", _factory.PaymentSessionService.LastSessionIdRequested);

            var status = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == TestDataSeeder.OrderHeaderAId).PaymentStatus);
            Assert.Equal("Approved", status);
        }

        [Fact]
        public async Task ForeignOrder_IsDenied_BeforeAnyStripeCall()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            // OrderHeaderBId appartient à Customer B : IDOR historique (SEC-004).
            var response = await client.GetAsync($"/Cart/OrderConfirmation?id={TestDataSeeder.OrderHeaderBId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            // Preuve requise par la section 17 : aucun appel Stripe pour une commande
            // non autorisée.
            Assert.Equal(0, _factory.PaymentSessionService.CallCount);

            // Et aucun effet de bord : le statut et le panier de B restent inchangés.
            var status = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == TestDataSeeder.OrderHeaderBId).PaymentStatus);
            Assert.Equal("Pending", status);
            var cartStillExists = _factory.Query(ctx => ctx.ShoppingCarts.Any(c => c.Id == TestDataSeeder.ShoppingCartBId));
            Assert.True(cartStillExists);
        }

        [Fact]
        public async Task AnonymousUser_IsDenied_BeforeAnyStripeCall()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync($"/Cart/OrderConfirmation?id={TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(0, _factory.PaymentSessionService.CallCount);
        }

        [Fact]
        public async Task Admin_CanConfirmAnyOrder()
        {
            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.GetAsync($"/Cart/OrderConfirmation?id={TestDataSeeder.OrderHeaderBId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, _factory.PaymentSessionService.CallCount);
        }

        [Fact]
        public async Task NonExistentOrder_ReturnsNotFound_NoNullReferenceException_NoStripeCall()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/Cart/OrderConfirmation?id={TestDataSeeder.NonExistentId}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(0, _factory.PaymentSessionService.CallCount);
        }

        public void Dispose() => _factory.Dispose();
    }
}
