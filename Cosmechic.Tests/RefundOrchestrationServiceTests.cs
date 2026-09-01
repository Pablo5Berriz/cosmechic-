using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 70) : remboursement complet/partiel,
    // solde remboursable, idempotence, échec/retry Stripe.
    public class RefundOrchestrationServiceTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly FakeStripeRefundService _stripeRefundService = new();
        private readonly RefundOrchestrationService _sut;

        public RefundOrchestrationServiceTests()
        {
            _sut = new RefundOrchestrationService(
                _context, _stripeRefundService, new OrderLifecycleService(_context), NullLogger<RefundOrchestrationService>.Instance);
        }

        private OrderHeader SeedPaidOrder(decimal orderTotal = 100m)
        {
            var order = new OrderHeader
            {
                ApplicationUserId = "user-a",
                OrderDate = DateTime.UtcNow,
                OrderTotal = orderTotal,
                Subtotal = orderTotal,
                OrderStatus = SD.OrderStatusConfirmed,
                PaymentStatus = SD.PaymentStatusPaid,
                FulfillmentStatus = SD.FulfillmentStatusProcessing,
                PaymentIntentId = "pi_test",
                Name = "Test",
                PhoneNumber = "5145551234",
                StreetAddress = "1 rue Test",
                City = "Montreal",
                State = "QC",
                PostalCode = "H0H0H0",
            };
            _context.OrderHeaders.Add(order);
            _context.SaveChanges();
            return order;
        }

        [Fact]
        public async Task FullRefund_Succeeds_MarksOrderRefunded()
        {
            var order = SeedPaidOrder(100m);

            var result = await _sut.RequestRefundAsync(order.Id, 100m, null, "full refund", "admin-1", SD.ActorTypeAdmin);

            var succeeded = Assert.IsType<RefundSucceeded>(result);
            Assert.Equal(1, _stripeRefundService.CallCount);
            Assert.Equal(10000, _stripeRefundService.LastOptions!.Amount);

            var reloaded = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == order.Id);
            Assert.Equal(100m, reloaded.RefundedAmount);
            Assert.Equal(SD.PaymentStatusRefunded, reloaded.PaymentStatus);

            var refund = _context.Refunds.AsNoTracking().Single(r => r.Id == succeeded.RefundId);
            Assert.Equal(SD.RefundStatusSucceeded, refund.Status);
            Assert.NotNull(refund.StripeRefundId);
        }

        // COSMECHIC-COMMERCE-OPERATIONS-001B (section 78/80) : régression COMMERCE-001A —
        // un remboursement ne modifie jamais le snapshot financier historique de la
        // commande. Seul RefundedAmount (et, dérivé, PaymentStatus) change.
        [Fact]
        public async Task Refund_NeverMutatesOrderFinancialSnapshot()
        {
            var order = SeedPaidOrder(100m);
            var beforeSubtotal = order.Subtotal;
            var beforeShipping = order.ShippingAmount;
            var beforeTax = order.TaxAmount;
            var beforeDiscount = order.DiscountAmount;
            var beforeTotal = order.OrderTotal;

            await _sut.RequestRefundAsync(order.Id, 40m, null, "partial", "admin-1", SD.ActorTypeAdmin);

            var reloaded = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == order.Id);
            Assert.Equal(beforeSubtotal, reloaded.Subtotal);
            Assert.Equal(beforeShipping, reloaded.ShippingAmount);
            Assert.Equal(beforeTax, reloaded.TaxAmount);
            Assert.Equal(beforeDiscount, reloaded.DiscountAmount);
            Assert.Equal(beforeTotal, reloaded.OrderTotal);
        }

        [Fact]
        public async Task PartialRefund_Succeeds_MarksOrderPartiallyRefunded()
        {
            var order = SeedPaidOrder(100m);

            var result = await _sut.RequestRefundAsync(order.Id, 40m, null, "partial", "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundSucceeded>(result);
            var reloaded = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == order.Id);
            Assert.Equal(40m, reloaded.RefundedAmount);
            Assert.Equal(SD.PaymentStatusPartiallyRefunded, reloaded.PaymentStatus);
        }

        [Fact]
        public async Task RefundExceedingBalance_IsRejected_NoStripeCall()
        {
            var order = SeedPaidOrder(100m);

            var result = await _sut.RequestRefundAsync(order.Id, 150m, null, null, "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundRejected>(result);
            Assert.Equal(0, _stripeRefundService.CallCount);
        }

        [Fact]
        public async Task SecondRefundExceedingRemainingBalance_IsRejected()
        {
            var order = SeedPaidOrder(100m);
            await _sut.RequestRefundAsync(order.Id, 70m, null, null, "admin-1", SD.ActorTypeAdmin);

            var result = await _sut.RequestRefundAsync(order.Id, 50m, null, null, "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundRejected>(result);
            var reloaded = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == order.Id);
            Assert.Equal(70m, reloaded.RefundedAmount);
        }

        [Fact]
        public async Task AlreadyFullyRefundedOrder_FurtherRefund_IsRejected()
        {
            var order = SeedPaidOrder(100m);
            await _sut.RequestRefundAsync(order.Id, 100m, null, null, "admin-1", SD.ActorTypeAdmin);

            var result = await _sut.RequestRefundAsync(order.Id, 0.01m, null, null, "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundRejected>(result);
        }

        [Fact]
        public async Task StripeFailure_ReleasesReservedBalance_MarksRefundFailed()
        {
            var order = SeedPaidOrder(100m);
            _stripeRefundService.ExceptionToThrow = new InvalidOperationException("simulated Stripe outage");

            var result = await _sut.RequestRefundAsync(order.Id, 100m, null, null, "admin-1", SD.ActorTypeAdmin);

            var failed = Assert.IsType<RefundFailed>(result);
            var reloaded = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == order.Id);
            // Solde libéré (section 37) : une nouvelle tentative doit rester possible.
            Assert.Equal(0m, reloaded.RefundedAmount);
            Assert.Equal(SD.PaymentStatusPaid, reloaded.PaymentStatus);

            var refund = _context.Refunds.AsNoTracking().Single(r => r.Id == failed.RefundId);
            Assert.Equal(SD.RefundStatusFailed, refund.Status);
        }

        [Fact]
        public async Task RetryFailedRefund_ReusesSameIdempotencyKey_Succeeds()
        {
            var order = SeedPaidOrder(100m);
            _stripeRefundService.ExceptionToThrow = new InvalidOperationException("simulated Stripe outage");
            var failedResult = (RefundFailed)await _sut.RequestRefundAsync(order.Id, 100m, null, null, "admin-1", SD.ActorTypeAdmin);
            var originalKey = _context.Refunds.AsNoTracking().Single(r => r.Id == failedResult.RefundId).IdempotencyKey;

            _stripeRefundService.ExceptionToThrow = null;
            var retryResult = await _sut.RetryFailedRefundAsync(failedResult.RefundId, "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundSucceeded>(retryResult);
            Assert.Equal(2, _stripeRefundService.CallCount);
            Assert.All(_stripeRefundService.IdempotencyKeysSeen, key => Assert.Equal(originalKey, key));

            var reloaded = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == order.Id);
            Assert.Equal(100m, reloaded.RefundedAmount);
        }

        [Fact]
        public async Task RetryingASucceededRefund_IsRejected()
        {
            var order = SeedPaidOrder(100m);
            var succeeded = (RefundSucceeded)await _sut.RequestRefundAsync(order.Id, 100m, null, null, "admin-1", SD.ActorTypeAdmin);

            var result = await _sut.RetryFailedRefundAsync(succeeded.RefundId, "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundRejected>(result);
        }

        [Fact]
        public async Task NoPaymentIntent_RefundIsRejected()
        {
            var order = SeedPaidOrder(100m);
            var toUpdate = await _context.OrderHeaders.FirstAsync(o => o.Id == order.Id);
            toUpdate.PaymentIntentId = null;
            await _context.SaveChangesAsync();

            var result = await _sut.RequestRefundAsync(order.Id, 50m, null, null, "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundRejected>(result);
            Assert.Equal(0, _stripeRefundService.CallCount);
        }

        public void Dispose() => _context.Dispose();
    }
}
