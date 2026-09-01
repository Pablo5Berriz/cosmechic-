namespace Cosmechic.Services
{
    public abstract record CancellationResult;
    public sealed record CancellationSucceeded(bool RefundTriggered) : CancellationResult;
    public sealed record CancellationRejected(string Reason) : CancellationResult;

    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 12/13/14) : politique technique minimale
    // — jamais une règle commerciale arbitraire (ex. délai de rétractation) inventée ici.
    // Bloque uniquement : déjà expédiée, déjà annulée, déjà entièrement remboursée. Une
    // commande payée passe TOUJOURS par le workflow de remboursement (jamais un simple
    // changement de statut qui ignorerait la réalité financière, section 14).
    public interface ICancellationService
    {
        Task<CancellationResult> CancelOrderAsync(int orderId, string requestingUserId, bool isAdmin, string? reason);
    }
}
