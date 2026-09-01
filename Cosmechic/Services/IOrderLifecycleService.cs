using Cosmechic.Models;

namespace Cosmechic.Services
{
    public abstract record LifecycleTransitionResult;
    public sealed record LifecycleTransitionApplied : LifecycleTransitionResult;
    public sealed record LifecycleTransitionRejected(string Reason) : LifecycleTransitionResult;

    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 2/8/9) : seule autorité habilitée à muter
    // OrderHeader.OrderStatus / PaymentStatus / FulfillmentStatus. Aucun controller ni
    // service ne doit plus jamais écrire `order.OrderStatus = "..."` directement — soit la
    // transition est valide et appliquée + auditée ici, soit elle est refusée avec une
    // raison explicite.
    //
    // Volontairement SANS SaveChangesAsync : chaque méthode STAGE la mutation sur l'entité
    // suivie et AJOUTE (context.Add, sans committer) la ligne OrderStatusHistory
    // correspondante, laissant l'appelant persister le tout dans SA propre transaction
    // (ex. StripeFulfillmentService gère déjà une boucle de retry sur conflit de
    // concurrence RowVersion — ce service compose avec elle plutôt que de la dupliquer).
    public interface IOrderLifecycleService
    {
        void ApplyOrderCreated(OrderHeader order);

        LifecycleTransitionResult TryTransitionOrderStatus(
            OrderHeader order, string newStatus, string? reason, string? actorUserId, string actorType);

        LifecycleTransitionResult TryTransitionPaymentStatus(
            OrderHeader order, string newStatus, string? reason, string? actorUserId, string actorType);

        LifecycleTransitionResult TryTransitionFulfillmentStatus(
            OrderHeader order, string newStatus, string? reason, string? actorUserId, string actorType);

        // Événements sans dimension de statut correspondante à eux seuls (retour demandé,
        // remboursement déclenché...) — même piste d'audit, pas de validation de transition
        // (section 10 : "qui/quoi/quand/pourquoi" pour tout événement métier pertinent).
        void RecordEvent(OrderHeader order, string eventType, string? previousStatus, string? newStatus, string? reason, string? actorUserId, string actorType);
    }
}
