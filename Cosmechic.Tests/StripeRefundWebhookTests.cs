using System.Net;
using Cosmechic.Models;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 75) : refund.updated au travers du vrai
    // pipeline HTTP (signature réelle, même contrôleur/route que les événements checkout).
    public class StripeRefundWebhookTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();
        private readonly HttpClient _client;

        public StripeRefundWebhookTests()
        {
            _client = _factory.CreateTestClient();
        }

        private (int OrderId, int RefundId) SeedPendingRefund(decimal orderTotal = 100m, decimal refundAmount = 100m)
        {
            var orderId = 0;
            var refundId = 0;

            _factory.Seed(context =>
            {
                context.AspNetUsers.Add(new AspNetUser { Id = "user-refund-webhook", UserName = "user-refund-webhook" });

                var order = new OrderHeader
                {
                    ApplicationUserId = "user-refund-webhook",
                    OrderDate = DateTime.UtcNow,
                    OrderTotal = orderTotal,
                    Subtotal = orderTotal,
                    OrderStatus = SD.OrderStatusConfirmed,
                    PaymentStatus = SD.PaymentStatusPaid,
                    FulfillmentStatus = SD.FulfillmentStatusProcessing,
                    PaymentIntentId = "pi_test",
                    RefundedAmount = refundAmount,
                    Name = "Test",
                    PhoneNumber = "5145551234",
                    StreetAddress = "1 rue Test",
                    City = "Montreal",
                    State = "QC",
                    PostalCode = "H0H0H0",
                };
                context.OrderHeaders.Add(order);
                context.SaveChanges();
                orderId = order.Id;

                // Simule une réservation déjà committée (RefundOrchestrationService,
                // section 1 de sa conception) dont la finalisation synchrone n'a jamais pu
                // s'exécuter — c'est exactement le scénario que le webhook doit résoudre.
                var refund = new Refund
                {
                    OrderId = order.Id,
                    IdempotencyKey = Guid.NewGuid().ToString("N"),
                    Amount = refundAmount,
                    Status = SD.RefundStatusPending,
                    ActorType = SD.ActorTypeAdmin,
                    CreatedAt = DateTime.UtcNow,
                };
                context.Refunds.Add(refund);
                context.SaveChanges();
                refundId = refund.Id;
            });

            return (orderId, refundId);
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
        public async Task ValidSignedSucceededEvent_FinalizesRefund_UpdatesOrderPaymentStatus()
        {
            var (orderId, refundId) = SeedPendingRefund(orderTotal: 100m, refundAmount: 100m);
            var payload = StripeEventJsonBuilder.RefundUpdatedEvent(
                "evt_refund_ok", "re_test_1", "succeeded", 10000, refundId);

            var response = await PostWebhookAsync(payload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var refund = _factory.Query(ctx => ctx.Refunds.First(r => r.Id == refundId));
            Assert.Equal(SD.RefundStatusSucceeded, refund.Status);
            Assert.Equal("re_test_1", refund.StripeRefundId);

            var order = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == orderId));
            Assert.Equal(SD.PaymentStatusRefunded, order.PaymentStatus);
        }

        [Fact]
        public async Task InvalidSignature_Returns400_NoMutation()
        {
            var (_, refundId) = SeedPendingRefund();
            var payload = StripeEventJsonBuilder.RefundUpdatedEvent("evt_refund_badsig", "re_test_2", "succeeded", 10000, refundId);

            var response = await PostWebhookAsync(payload, signatureOverride: "t=1700000000,v1=deadbeef00000000000000000000000000000000000000000000000000000000");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var refund = _factory.Query(ctx => ctx.Refunds.First(r => r.Id == refundId));
            Assert.Equal(SD.RefundStatusPending, refund.Status);
        }

        [Fact]
        public async Task DuplicateEvent_SentTwice_ProducesSingleEffect()
        {
            var (_, refundId) = SeedPendingRefund();
            var payload = StripeEventJsonBuilder.RefundUpdatedEvent("evt_refund_dup", "re_test_3", "succeeded", 10000, refundId);

            var first = await PostWebhookAsync(payload);
            var second = await PostWebhookAsync(payload);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var refund = _factory.Query(ctx => ctx.Refunds.First(r => r.Id == refundId));
            Assert.Equal(SD.RefundStatusSucceeded, refund.Status);
        }

        [Fact]
        public async Task UnknownRefund_Returns200_NoException()
        {
            var payload = StripeEventJsonBuilder.RefundUpdatedEvent("evt_refund_unknown", "re_unknown", "succeeded", 10000, refundRecordId: 999999);

            var response = await PostWebhookAsync(payload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AlreadyCompletedRefund_LateEvent_IsIdempotentNoOp()
        {
            var (orderId, refundId) = SeedPendingRefund(orderTotal: 100m, refundAmount: 100m);
            var firstPayload = StripeEventJsonBuilder.RefundUpdatedEvent("evt_refund_first", "re_test_4", "succeeded", 10000, refundId);
            await PostWebhookAsync(firstPayload);

            // Événement tardif distinct (ex. retry Stripe) pour le même Refund déjà finalisé.
            var latePayload = StripeEventJsonBuilder.RefundUpdatedEvent("evt_refund_late", "re_test_4", "succeeded", 10000, refundId);
            var response = await PostWebhookAsync(latePayload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var refund = _factory.Query(ctx => ctx.Refunds.First(r => r.Id == refundId));
            Assert.Equal(SD.RefundStatusSucceeded, refund.Status);
            var order = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == orderId));
            Assert.Equal(SD.PaymentStatusRefunded, order.PaymentStatus);
        }

        [Fact]
        public async Task FailedStripeEvent_ReleasesReservedBalance()
        {
            var (orderId, refundId) = SeedPendingRefund(orderTotal: 100m, refundAmount: 100m);
            var payload = StripeEventJsonBuilder.RefundUpdatedEvent(
                "evt_refund_failed", "re_test_5", "failed", 10000, refundId, failureReason: "expired_or_canceled_card");

            var response = await PostWebhookAsync(payload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var refund = _factory.Query(ctx => ctx.Refunds.First(r => r.Id == refundId));
            Assert.Equal(SD.RefundStatusFailed, refund.Status);

            var order = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == orderId));
            Assert.Equal(0m, order.RefundedAmount);
        }

        public void Dispose() => _factory.Dispose();
    }
}
