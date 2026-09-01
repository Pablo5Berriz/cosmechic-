namespace Cosmechic.Services
{
    public enum RefundWebhookOutcome
    {
        Processed,
        AlreadyProcessed,
    }

    public abstract record RefundResult;
    public sealed record RefundSucceeded(int RefundId, string StripeRefundId) : RefundResult;
    public sealed record RefundPendingStripeConfirmation(int RefundId) : RefundResult;
    public sealed record RefundFailed(int RefundId, string Reason) : RefundResult;
    public sealed record RefundRejected(string Reason) : RefundResult;

    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 21/27/28/35) : un remboursement Stripe
    // n'est jamais une simple mise à jour de statut. C'est une opération externe,
    // persistante, idempotente, réessayable et auditable — voir
    // docs/audits/COSMECHIC-COMMERCE-OPERATIONS-001B.md pour la conception complète de la
    // frontière DB/Stripe (aucune transaction distribuée n'est tentée).
    public interface IRefundOrchestrationService
    {
        Task<RefundResult> RequestRefundAsync(
            int orderId, decimal amount, int? returnRequestId, string? reason, string? requestedByUserId, string actorType);

        // Réutilise la MÊME IdempotencyKey déjà persistée : si le premier appel Stripe avait
        // en réalité réussi malgré une erreur perçue côté serveur, Stripe renvoie le résultat
        // déjà obtenu plutôt que de créer un second remboursement réel (section 37).
        Task<RefundResult> RetryFailedRefundAsync(int refundId, string? actorUserId, string actorType);

        // Convergence asynchrone (section 31/37) : le webhook Stripe est la voie de secours
        // qui finalise un Refund resté Pending si la réponse synchrone n'a jamais pu être
        // traitée (Stripe a réussi, mais l'écriture DB qui a suivi l'appel a échoué).
        Task ReconcileFromStripeEventAsync(string? stripeRefundId, int? refundRecordId, string stripeStatus, string? failureCode);

        // Point d'entrée appelé par StripeWebhookController pour un événement refund.updated
        // déjà authentifié (signature vérifiée par l'appelant) : applique la même barrière
        // d'idempotence à deux niveaux (ProcessedStripeEvent) que les événements checkout
        // (section 33 — "1 Stripe event = 1 effet métier"), puis délègue à
        // ReconcileFromStripeEventAsync.
        Task<RefundWebhookOutcome> ProcessRefundEventAsync(
            string stripeEventId, string eventType, string? stripeRefundId, int? refundRecordId, string stripeStatus, string? failureCode);
    }
}
