using Cosmechic.Models;

namespace Cosmechic.Services
{
    public abstract record CancellationResult;
    public sealed record CancellationSucceeded(bool RefundTriggered) : CancellationResult;
    public sealed record CancellationRejected(string Reason) : CancellationResult;

    public abstract record CancellationEligibility;
    public sealed record CancellationEligible : CancellationEligibility;
    public sealed record CancellationIneligible(string Reason) : CancellationEligibility;

    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 12/13/14) : politique technique minimale
    // — jamais une règle commerciale arbitraire (ex. délai de rétractation) inventée ici.
    // Bloque uniquement : déjà expédiée, déjà annulée, déjà entièrement remboursée. Une
    // commande payée passe TOUJOURS par le workflow de remboursement (jamais un simple
    // changement de statut qui ignorerait la réalité financière, section 14).
    public interface ICancellationService
    {
        Task<CancellationResult> CancelOrderAsync(int orderId, string requestingUserId, bool isAdmin, string? reason);

        // COSMECHIC-ACCOUNT-001 (section 20) : même politique technique que
        // CancelOrderAsync (sans l'ownership, à la charge de l'appelant), exposée en
        // lecture seule pour qu'une vue affiche/masque le bouton "Annuler" sans dupliquer
        // la règle — même motif que IReturnService.CanRequestReturnAsync.
        CancellationEligibility CanCancel(OrderHeader order);
    }
}
