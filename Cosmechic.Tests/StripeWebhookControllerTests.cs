using System.Net;
using Cosmechic.Models;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-ECOM-CORE-001 (section 33) : preuves de bout en bout (vraie pipeline HTTP
    // ASP.NET Core via CustomWebApplicationFactory) pour StripeWebhookController. Chaque
    // payload est signé localement (StripeSignatureTestHelper) — aucun appel réseau réel
    // à Stripe, aucune clé live (section 37).
    public class StripeWebhookControllerTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();
        private readonly HttpClient _client;

        public StripeWebhookControllerTests()
        {
            _client = _factory.CreateTestClient();
        }

        private (int OrderId, int ProduitId) SeedPendingOrder(decimal prix, decimal stock, int count, string sessionId)
        {
            var orderId = 0;
            var produitId = 0;

            _factory.Seed(context =>
            {
                var categorie = new Category { Nom = "Cat", Image = "c.jpg", Disponible = true };
                context.Categories.Add(categorie);
                context.SaveChanges();

                var produit = new Produit
                {
                    Nom = "Creme",
                    CategorieId = categorie.CategorieId,
                    Prix = prix,
                    Stock = stock,
                    Disponible = true,
                    Image = "p.jpg",
                    RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
                };
                context.Produits.Add(produit);
                context.SaveChanges();
                produitId = produit.ProduitId;

                context.AspNetUsers.Add(new AspNetUser { Id = "user-webhook-test", UserName = "user-webhook-test" });

                var order = new OrderHeader
                {
                    ApplicationUserId = "user-webhook-test",
                    OrderDate = DateTime.UtcNow,
                    OrderTotal = prix * count,
                    Subtotal = prix * count,
                    OrderStatus = SD.StatusPending,
                    PaymentStatus = SD.PaymentStatusPending,
                    SessionId = sessionId,
                    Name = "Test",
                    PhoneNumber = "5145551234",
                    StreetAddress = "1 rue Test",
                    City = "Montreal",
                    State = "QC",
                    PostalCode = "H0H0H0",
                };
                order.OrderDetails.Add(new OrderDetail { ProduitId = produit.ProduitId, Count = count, Price = prix });
                context.OrderHeaders.Add(order);
                context.SaveChanges();
                orderId = order.Id;
            });

            return (orderId, produitId);
        }

        private async Task<HttpResponseMessage> PostWebhookAsync(string payload, string? signatureOverride = null)
        {
            var signature = signatureOverride ?? StripeSignatureTestHelper.SignPayload(payload, CustomWebApplicationFactory.TestWebhookSecret);
            var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Stripe-Signature", signature);
            return await _client.SendAsync(request);
        }

        [Fact]
        public async Task ValidSignature_SuccessfulPayment_Returns200_FulfillsOrder()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 2, sessionId: "cs_test_valid");
            var payload = StripeEventJsonBuilder.CheckoutSessionEvent(
                "evt_valid", "checkout.session.completed", "cs_test_valid", orderId, amountTotal: 5000);

            var response = await PostWebhookAsync(payload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var order = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == orderId));
            Assert.Equal(SD.PaymentStatusApproved, order.PaymentStatus);
            Assert.Equal(SD.StatusInProcess, order.OrderStatus);
            var stock = _factory.Query(ctx => ctx.Produits.First(p => p.ProduitId == produitId).Stock);
            Assert.Equal(8, stock);
        }

        [Fact]
        public async Task InvalidSignature_Returns400_NoMutation()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 1, sessionId: "cs_test_badsig");
            var payload = StripeEventJsonBuilder.CheckoutSessionEvent(
                "evt_badsig", "checkout.session.completed", "cs_test_badsig", orderId, amountTotal: 2500);

            var response = await PostWebhookAsync(payload, signatureOverride: "t=1700000000,v1=deadbeef00000000000000000000000000000000000000000000000000000000");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var order = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == orderId));
            Assert.Equal(SD.PaymentStatusPending, order.PaymentStatus);
            var stock = _factory.Query(ctx => ctx.Produits.First(p => p.ProduitId == produitId).Stock);
            Assert.Equal(10, stock);
            Assert.False(_factory.Query(ctx => ctx.ProcessedStripeEvents.Any(e => e.StripeEventId == "evt_badsig")));
        }

        [Fact]
        public async Task DuplicateEvent_SentTwice_ProducesSingleBusinessEffect()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 1, sessionId: "cs_test_dup2");
            var payload = StripeEventJsonBuilder.CheckoutSessionEvent(
                "evt_dup2", "checkout.session.completed", "cs_test_dup2", orderId, amountTotal: 2500);

            var first = await PostWebhookAsync(payload);
            var second = await PostWebhookAsync(payload);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var stock = _factory.Query(ctx => ctx.Produits.First(p => p.ProduitId == produitId).Stock);
            Assert.Equal(9, stock);
        }

        [Fact]
        public async Task UnsupportedEventType_Returns200_NoProcessing()
        {
            var payload = StripeEventJsonBuilder.UnsupportedEvent("evt_unsupported");

            var response = await PostWebhookAsync(payload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(_factory.Query(ctx => ctx.ProcessedStripeEvents.Any(e => e.StripeEventId == "evt_unsupported")));
        }

        [Fact]
        public async Task MissingOrder_Returns200_NoException()
        {
            var payload = StripeEventJsonBuilder.CheckoutSessionEvent(
                "evt_missing_order", "checkout.session.completed", "cs_test_missing", orderId: 999999, amountTotal: 2500);

            var response = await PostWebhookAsync(payload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AmountMismatch_Returns200_OrderStaysPending()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 1, sessionId: "cs_test_wrongamount");
            var payload = StripeEventJsonBuilder.CheckoutSessionEvent(
                "evt_wrongamount", "checkout.session.completed", "cs_test_wrongamount", orderId, amountTotal: 100);

            var response = await PostWebhookAsync(payload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var order = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == orderId));
            Assert.Equal(SD.PaymentStatusPending, order.PaymentStatus);
            var stock = _factory.Query(ctx => ctx.Produits.First(p => p.ProduitId == produitId).Stock);
            Assert.Equal(10, stock);
        }

        [Fact]
        public async Task CurrencyMismatch_Returns200_OrderStaysPending()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 1, sessionId: "cs_test_wrongcur");
            var payload = StripeEventJsonBuilder.CheckoutSessionEvent(
                "evt_wrongcur", "checkout.session.completed", "cs_test_wrongcur", orderId, amountTotal: 2500, currency: "usd");

            var response = await PostWebhookAsync(payload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var order = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == orderId));
            Assert.Equal(SD.PaymentStatusPending, order.PaymentStatus);
        }

        [Fact]
        public async Task AlreadyProcessedOrder_SecondDistinctEvent_Returns200_NoDoubleFulfillment()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 1, sessionId: "cs_test_already");
            var firstPayload = StripeEventJsonBuilder.CheckoutSessionEvent(
                "evt_already_1", "checkout.session.completed", "cs_test_already", orderId, amountTotal: 2500);
            await PostWebhookAsync(firstPayload);

            var secondPayload = StripeEventJsonBuilder.CheckoutSessionEvent(
                "evt_already_2", "checkout.session.async_payment_succeeded", "cs_test_already", orderId, amountTotal: 2500);
            var response = await PostWebhookAsync(secondPayload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var stock = _factory.Query(ctx => ctx.Produits.First(p => p.ProduitId == produitId).Stock);
            Assert.Equal(9, stock);
        }

        [Fact]
        public async Task PaymentFailedEvent_Returns200_CancelsOrder_NoStockChange()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 1, sessionId: "cs_test_failed");
            var payload = StripeEventJsonBuilder.CheckoutSessionEvent(
                "evt_failed", "checkout.session.async_payment_failed", "cs_test_failed", orderId, amountTotal: 2500,
                paymentStatus: "unpaid");

            var response = await PostWebhookAsync(payload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var order = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == orderId));
            Assert.Equal(SD.PaymentStatusRejected, order.PaymentStatus);
            var stock = _factory.Query(ctx => ctx.Produits.First(p => p.ProduitId == produitId).Stock);
            Assert.Equal(10, stock);
        }

        public void Dispose() => _factory.Dispose();
    }
}
