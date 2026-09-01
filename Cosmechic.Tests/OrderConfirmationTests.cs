using System.Net;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-SECURITY-001 (contrôle d'ownership, section 16/17) toujours en vigueur,
    // adapté par COSMECHIC-ECOM-CORE-001 (section 11) : CartController.OrderConfirmation
    // est désormais une vue d'état pure — elle ne fait plus jamais aucun appel Stripe ni
    // aucune mutation (PaymentStatus/OrderStatus/panier). La preuve automatisée porte
    // maintenant sur l'absence totale d'effet de bord, pas seulement sur l'absence
    // d'appel Stripe pour une commande étrangère.
    public class OrderConfirmationTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public OrderConfirmationTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        [Fact]
        public async Task OwnOrder_ReachesAuthorizedFlow_WithoutAnyStripeCallOrMutation()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/Cart/OrderConfirmation?id={TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // La vue de confirmation ne crée plus jamais de session Stripe (ça reste le
            // rôle exclusif de CheckoutService) ni ne mute le paiement : seul le webhook
            // signé peut désormais le faire.
            Assert.Equal(0, _factory.StripeCheckoutService.CallCount);

            var status = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == TestDataSeeder.OrderHeaderAId).PaymentStatus);
            Assert.Equal("Pending", status);

            // Le panier de A n'est plus vidé par le simple affichage de la confirmation —
            // seul un fulfillment webhook vérifié le fait désormais (section 25).
            var cartStillExists = _factory.Query(ctx => ctx.ShoppingCarts.Any(c => c.Id == TestDataSeeder.ShoppingCartAId));
            Assert.True(cartStillExists);
        }

        [Fact]
        public async Task ForeignOrder_IsDenied_WithoutAnyStripeCallOrMutation()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            // OrderHeaderBId appartient à Customer B : IDOR historique (SEC-004).
            var response = await client.GetAsync($"/Cart/OrderConfirmation?id={TestDataSeeder.OrderHeaderBId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(0, _factory.StripeCheckoutService.CallCount);

            var status = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == TestDataSeeder.OrderHeaderBId).PaymentStatus);
            Assert.Equal("Pending", status);
            var cartStillExists = _factory.Query(ctx => ctx.ShoppingCarts.Any(c => c.Id == TestDataSeeder.ShoppingCartBId));
            Assert.True(cartStillExists);
        }

        [Fact]
        public async Task AnonymousUser_IsDenied()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync($"/Cart/OrderConfirmation?id={TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(0, _factory.StripeCheckoutService.CallCount);
        }

        [Fact]
        public async Task Admin_CanViewAnyOrder()
        {
            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.GetAsync($"/Cart/OrderConfirmation?id={TestDataSeeder.OrderHeaderBId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, _factory.StripeCheckoutService.CallCount);
        }

        [Fact]
        public async Task NonExistentOrder_ReturnsNotFound_NoNullReferenceException()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/Cart/OrderConfirmation?id={TestDataSeeder.NonExistentId}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(0, _factory.StripeCheckoutService.CallCount);
        }

        public void Dispose() => _factory.Dispose();
    }
}
