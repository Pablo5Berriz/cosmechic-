using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 68) : annulation avant/après paiement.
    public class CancellationServiceTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly FakeStripeRefundService _stripeRefundService = new();
        private readonly CancellationService _sut;
        private readonly RefundOrchestrationService _refundService;

        public CancellationServiceTests()
        {
            var lifecycleService = new OrderLifecycleService(_context);
            _refundService = new RefundOrchestrationService(
                _context, _stripeRefundService, lifecycleService, NullLogger<RefundOrchestrationService>.Instance);
            _sut = new CancellationService(_context, lifecycleService, _refundService);
        }

        private OrderHeader SeedOrder(string orderStatus, string paymentStatus, string fulfillmentStatus, string? paymentIntentId = "pi_test")
        {
            var order = new OrderHeader
            {
                ApplicationUserId = "user-a",
                OrderDate = DateTime.UtcNow,
                OrderTotal = 100m,
                Subtotal = 100m,
                OrderStatus = orderStatus,
                PaymentStatus = paymentStatus,
                FulfillmentStatus = fulfillmentStatus,
                PaymentIntentId = paymentIntentId,
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
        public async Task PendingUnpaidOrder_Cancel_Succeeds_NoRefundTriggered()
        {
            var order = SeedOrder(SD.OrderStatusPending, SD.PaymentStatusPending, SD.FulfillmentStatusUnfulfilled, paymentIntentId: null);

            var result = await _sut.CancelOrderAsync(order.Id, "user-a", isAdmin: false, "changed my mind");

            var succeeded = Assert.IsType<CancellationSucceeded>(result);
            Assert.False(succeeded.RefundTriggered);
            Assert.Equal(0, _stripeRefundService.CallCount);

            var reloaded = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == order.Id);
            Assert.Equal(SD.OrderStatusCancelled, reloaded.OrderStatus);
        }

        [Fact]
        public async Task PaidOrder_Cancel_TriggersRefund_ForFullRefundableBalance()
        {
            var order = SeedOrder(SD.OrderStatusConfirmed, SD.PaymentStatusPaid, SD.FulfillmentStatusProcessing);

            var result = await _sut.CancelOrderAsync(order.Id, "admin-1", isAdmin: true, "out of stock");

            var succeeded = Assert.IsType<CancellationSucceeded>(result);
            Assert.True(succeeded.RefundTriggered);
            Assert.Equal(1, _stripeRefundService.CallCount);
            Assert.Equal(100m, _stripeRefundService.LastOptions!.Amount / 100m);

            var reloaded = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == order.Id);
            Assert.Equal(SD.OrderStatusCancelled, reloaded.OrderStatus);
            Assert.Equal(100m, reloaded.RefundedAmount);
        }

        [Fact]
        public async Task ShippedOrder_Cancel_IsDenied()
        {
            var order = SeedOrder(SD.OrderStatusConfirmed, SD.PaymentStatusPaid, SD.FulfillmentStatusShipped);

            var result = await _sut.CancelOrderAsync(order.Id, "user-a", isAdmin: false, null);

            Assert.IsType<CancellationRejected>(result);
            Assert.Equal(0, _stripeRefundService.CallCount);
            var reloaded = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == order.Id);
            Assert.Equal(SD.OrderStatusConfirmed, reloaded.OrderStatus);
        }

        [Fact]
        public async Task AlreadyCancelledOrder_Cancel_IsIdempotentlyRejected()
        {
            var order = SeedOrder(SD.OrderStatusCancelled, SD.PaymentStatusFailed, SD.FulfillmentStatusCancelled);

            var result = await _sut.CancelOrderAsync(order.Id, "user-a", isAdmin: false, null);

            Assert.IsType<CancellationRejected>(result);
        }

        [Fact]
        public async Task FullyRefundedOrder_Cancel_IsDenied()
        {
            var order = SeedOrder(SD.OrderStatusConfirmed, SD.PaymentStatusRefunded, SD.FulfillmentStatusProcessing);

            var result = await _sut.CancelOrderAsync(order.Id, "admin-1", isAdmin: true, null);

            Assert.IsType<CancellationRejected>(result);
        }

        [Fact]
        public async Task ForeignOrder_Cancel_IsDeniedForCustomer()
        {
            var order = SeedOrder(SD.OrderStatusPending, SD.PaymentStatusPending, SD.FulfillmentStatusUnfulfilled, paymentIntentId: null);

            var result = await _sut.CancelOrderAsync(order.Id, "user-b", isAdmin: false, null);

            Assert.IsType<CancellationRejected>(result);
            var reloaded = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == order.Id);
            Assert.Equal(SD.OrderStatusPending, reloaded.OrderStatus);
        }

        // COSMECHIC-ACCOUNT-001 (section 20/44) : CanCancel exposé en lecture seule,
        // utilisé par les vues (OrderHeaders/Details, Account/OrderDetails) au lieu de
        // dupliquer la règle.
        [Fact]
        public void CanCancel_PendingUnpaidOrder_IsEligible()
        {
            var order = SeedOrder(SD.OrderStatusPending, SD.PaymentStatusPending, SD.FulfillmentStatusUnfulfilled, paymentIntentId: null);

            Assert.IsType<CancellationEligible>(_sut.CanCancel(order));
        }

        [Fact]
        public void CanCancel_AlreadyCancelled_IsIneligible()
        {
            var order = SeedOrder(SD.OrderStatusCancelled, SD.PaymentStatusPending, SD.FulfillmentStatusUnfulfilled, paymentIntentId: null);

            Assert.IsType<CancellationIneligible>(_sut.CanCancel(order));
        }

        [Fact]
        public void CanCancel_ShippedOrder_IsIneligible()
        {
            var order = SeedOrder(SD.OrderStatusConfirmed, SD.PaymentStatusPaid, SD.FulfillmentStatusShipped);

            Assert.IsType<CancellationIneligible>(_sut.CanCancel(order));
        }

        [Fact]
        public void CanCancel_FullyRefundedOrder_IsIneligible()
        {
            var order = SeedOrder(SD.OrderStatusConfirmed, SD.PaymentStatusRefunded, SD.FulfillmentStatusUnfulfilled);

            Assert.IsType<CancellationIneligible>(_sut.CanCancel(order));
        }

        public void Dispose() => _context.Dispose();
    }
}
