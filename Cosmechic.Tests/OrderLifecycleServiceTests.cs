using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 67) : matrice de transitions valides et
    // invalides pour les trois dimensions scalaires (OrderStatus/PaymentStatus/
    // FulfillmentStatus), jamais mélangées.
    public class OrderLifecycleServiceTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly OrderLifecycleService _sut;

        public OrderLifecycleServiceTests()
        {
            _sut = new OrderLifecycleService(_context);
        }

        private OrderHeader SeedOrder(string orderStatus, string paymentStatus, string fulfillmentStatus)
        {
            var order = new OrderHeader
            {
                ApplicationUserId = "user-a",
                OrderDate = DateTime.UtcNow,
                OrderTotal = 10m,
                Subtotal = 10m,
                OrderStatus = orderStatus,
                PaymentStatus = paymentStatus,
                FulfillmentStatus = fulfillmentStatus,
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
        public void ApplyOrderCreated_SetsAllThreeDimensionsToInitialValues()
        {
            var order = new OrderHeader();
            _sut.ApplyOrderCreated(order);

            Assert.Equal(SD.OrderStatusPending, order.OrderStatus);
            Assert.Equal(SD.PaymentStatusPending, order.PaymentStatus);
            Assert.Equal(SD.FulfillmentStatusUnfulfilled, order.FulfillmentStatus);
        }

        [Theory]
        [InlineData(SD.OrderStatusPending, SD.OrderStatusConfirmed)]
        [InlineData(SD.OrderStatusPending, SD.OrderStatusCancelled)]
        [InlineData(SD.OrderStatusConfirmed, SD.OrderStatusCancelled)]
        [InlineData(SD.OrderStatusConfirmed, SD.OrderStatusCompleted)]
        public void OrderStatus_ValidTransitions_AreApplied(string from, string to)
        {
            var order = SeedOrder(from, SD.PaymentStatusPending, SD.FulfillmentStatusUnfulfilled);

            var result = _sut.TryTransitionOrderStatus(order, to, "test", "user-a", SD.ActorTypeAdmin);

            Assert.IsType<LifecycleTransitionApplied>(result);
            Assert.Equal(to, order.OrderStatus);
        }

        [Theory]
        [InlineData(SD.OrderStatusCancelled, SD.OrderStatusConfirmed)]
        [InlineData(SD.OrderStatusCompleted, SD.OrderStatusPending)]
        [InlineData(SD.OrderStatusPending, SD.OrderStatusCompleted)]
        public void OrderStatus_InvalidTransitions_AreRejected_NoMutation(string from, string to)
        {
            var order = SeedOrder(from, SD.PaymentStatusPending, SD.FulfillmentStatusUnfulfilled);

            var result = _sut.TryTransitionOrderStatus(order, to, "test", "user-a", SD.ActorTypeAdmin);

            Assert.IsType<LifecycleTransitionRejected>(result);
            Assert.Equal(from, order.OrderStatus);
        }

        [Fact]
        public void OrderStatus_SameValue_IsIdempotentNoOp_NoHistoryWritten()
        {
            var order = SeedOrder(SD.OrderStatusPending, SD.PaymentStatusPending, SD.FulfillmentStatusUnfulfilled);

            var result = _sut.TryTransitionOrderStatus(order, SD.OrderStatusPending, "test", "user-a", SD.ActorTypeAdmin);

            Assert.IsType<LifecycleTransitionApplied>(result);
            Assert.Empty(_context.OrderStatusHistories.Where(h => h.OrderId == order.Id));
        }

        [Theory]
        [InlineData(SD.FulfillmentStatusCancelled, SD.FulfillmentStatusShipped)]
        [InlineData(SD.FulfillmentStatusDelivered, SD.FulfillmentStatusUnfulfilled)]
        public void FulfillmentStatus_InvalidTransitions_AreRejected(string from, string to)
        {
            var order = SeedOrder(SD.OrderStatusConfirmed, SD.PaymentStatusPaid, from);

            var result = _sut.TryTransitionFulfillmentStatus(order, to, "test", null, SD.ActorTypeAdmin);

            Assert.IsType<LifecycleTransitionRejected>(result);
            Assert.Equal(from, order.FulfillmentStatus);
        }

        [Fact]
        public void ValidTransition_WritesAuditHistoryRow_WithActorAndReason()
        {
            var order = SeedOrder(SD.OrderStatusPending, SD.PaymentStatusPending, SD.FulfillmentStatusUnfulfilled);

            _sut.TryTransitionOrderStatus(order, SD.OrderStatusCancelled, "Motif test", "user-a", SD.ActorTypeCustomer);
            _context.SaveChanges();

            var history = _context.OrderStatusHistories.Single(h => h.OrderId == order.Id);
            Assert.Equal("OrderStatusChanged", history.EventType);
            Assert.Equal(SD.OrderStatusPending, history.PreviousStatus);
            Assert.Equal(SD.OrderStatusCancelled, history.NewStatus);
            Assert.Equal("Motif test", history.Reason);
            Assert.Equal("user-a", history.ActorUserId);
            Assert.Equal(SD.ActorTypeCustomer, history.ActorType);
        }

        public void Dispose() => _context.Dispose();
    }
}
