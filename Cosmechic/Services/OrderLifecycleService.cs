using Cosmechic.Models;
using Cosmechic.Utility;

namespace Cosmechic.Services
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 7/9) : table de transitions valides pour
    // chacune des trois dimensions scalaires portées par OrderHeader. Volontairement
    // permissive uniquement là où une règle métier existe déjà (ex. Confirmed -> Cancelled
    // reste autorisé au niveau de la machine d'état : c'est CancellationService, pas cette
    // table, qui impose qu'un remboursement soit traité au préalable pour une commande déjà
    // payée — section 14).
    public class OrderLifecycleService(CosmechicsContext context) : IOrderLifecycleService
    {
        private static readonly Dictionary<string, HashSet<string>> OrderStatusTransitions = new()
        {
            [SD.OrderStatusPending] = new() { SD.OrderStatusConfirmed, SD.OrderStatusCancelled },
            [SD.OrderStatusConfirmed] = new() { SD.OrderStatusCancelled, SD.OrderStatusCompleted },
            [SD.OrderStatusCancelled] = new(),
            [SD.OrderStatusCompleted] = new(),
        };

        private static readonly Dictionary<string, HashSet<string>> PaymentStatusTransitions = new()
        {
            [SD.PaymentStatusPending] = new() { SD.PaymentStatusPaid, SD.PaymentStatusFailed },
            [SD.PaymentStatusPaid] = new() { SD.PaymentStatusPartiallyRefunded, SD.PaymentStatusRefunded },
            [SD.PaymentStatusPartiallyRefunded] = new() { SD.PaymentStatusPartiallyRefunded, SD.PaymentStatusRefunded },
            [SD.PaymentStatusRefunded] = new(),
            [SD.PaymentStatusFailed] = new(),
        };

        private static readonly Dictionary<string, HashSet<string>> FulfillmentStatusTransitions = new()
        {
            [SD.FulfillmentStatusUnfulfilled] = new() { SD.FulfillmentStatusProcessing, SD.FulfillmentStatusCancelled },
            [SD.FulfillmentStatusProcessing] = new() { SD.FulfillmentStatusShipped, SD.FulfillmentStatusCancelled },
            [SD.FulfillmentStatusShipped] = new() { SD.FulfillmentStatusDelivered },
            [SD.FulfillmentStatusDelivered] = new(),
            [SD.FulfillmentStatusCancelled] = new(),
        };

        public void ApplyOrderCreated(OrderHeader order)
        {
            order.OrderStatus = SD.OrderStatusPending;
            order.PaymentStatus = SD.PaymentStatusPending;
            order.FulfillmentStatus = SD.FulfillmentStatusUnfulfilled;
            // Pas d'entrée d'historique ici : OrderHeader n'a pas encore d'Id (pas encore
            // sauvegardé) — RecordEvent est appelé explicitement par l'appelant après le
            // premier SaveChangesAsync qui attribue l'Id (voir OrderCheckoutService).
        }

        public LifecycleTransitionResult TryTransitionOrderStatus(
            OrderHeader order, string newStatus, string? reason, string? actorUserId, string actorType)
        {
            return TryTransition(
                order, "OrderStatusChanged", order.OrderStatus, newStatus, OrderStatusTransitions,
                (o, s) => o.OrderStatus = s, reason, actorUserId, actorType);
        }

        public LifecycleTransitionResult TryTransitionPaymentStatus(
            OrderHeader order, string newStatus, string? reason, string? actorUserId, string actorType)
        {
            return TryTransition(
                order, "PaymentStatusChanged", order.PaymentStatus, newStatus, PaymentStatusTransitions,
                (o, s) => o.PaymentStatus = s, reason, actorUserId, actorType);
        }

        public LifecycleTransitionResult TryTransitionFulfillmentStatus(
            OrderHeader order, string newStatus, string? reason, string? actorUserId, string actorType)
        {
            return TryTransition(
                order, "FulfillmentStatusChanged", order.FulfillmentStatus, newStatus, FulfillmentStatusTransitions,
                (o, s) => o.FulfillmentStatus = s, reason, actorUserId, actorType);
        }

        private LifecycleTransitionResult TryTransition(
            OrderHeader order,
            string eventType,
            string? currentStatus,
            string newStatus,
            Dictionary<string, HashSet<string>> transitions,
            Action<OrderHeader, string> apply,
            string? reason,
            string? actorUserId,
            string actorType)
        {
            if (currentStatus == newStatus)
            {
                // Idempotent no-op explicite (ex. rejouer un événement Stripe déjà traité
                // par une autre voie) : pas une erreur, mais pas non plus une nouvelle
                // entrée d'historique pour une non-transition.
                return new LifecycleTransitionApplied();
            }

            if (currentStatus == null || !transitions.TryGetValue(currentStatus, out var allowed) || !allowed.Contains(newStatus))
            {
                return new LifecycleTransitionRejected(
                    $"Transition {eventType} refusée : {currentStatus ?? "(aucun)"} -> {newStatus} n'est pas autorisée.");
            }

            apply(order, newStatus);
            RecordEvent(order, eventType, currentStatus, newStatus, reason, actorUserId, actorType);
            return new LifecycleTransitionApplied();
        }

        public void RecordEvent(
            OrderHeader order, string eventType, string? previousStatus, string? newStatus, string? reason, string? actorUserId, string actorType)
        {
            context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                EventType = eventType,
                PreviousStatus = previousStatus,
                NewStatus = newStatus,
                Reason = reason,
                ActorUserId = actorUserId,
                ActorType = actorType,
                CreatedAt = DateTime.UtcNow,
            });
        }
    }
}
