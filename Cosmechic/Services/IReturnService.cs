using Cosmechic.Models;

namespace Cosmechic.Services
{
    public abstract record ReturnEligibility;
    public sealed record ReturnEligible : ReturnEligibility;
    public sealed record ReturnIneligible(string Reason) : ReturnEligibility;

    public abstract record ReturnRequestResult;
    public sealed record ReturnRequestCreated(int ReturnRequestId) : ReturnRequestResult;
    public sealed record ReturnRequestRejectedByPolicy(string Reason) : ReturnRequestResult;

    public abstract record ReturnActionResult;
    public sealed record ReturnActionApplied : ReturnActionResult;
    public sealed record ReturnActionRejected(string Reason) : ReturnActionResult;

    // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 5/6) : Category est la seule source
    // de vérité pour l'éligibilité et le routage d'une ligne — jamais Reason (texte libre,
    // complément narratif uniquement). IsOpened/IsUsed/CustomerDeclaredResellable ne sont
    // exigés (non-null) que pour ChangeOfMind ; laissés null pour les autres catégories.
    public sealed record ReturnItemInput(
        int OrderDetailId,
        int Quantity,
        string? Reason,
        ReturnReasonCategory Category,
        bool? IsOpened,
        bool? IsUsed,
        bool? CustomerDeclaredResellable);

    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 15-20) : demandes de retour, possiblement
    // partielles. La seule porte technique inconditionnelle est que la commande ait été
    // réellement expédiée/livrée : une commande jamais expédiée relève de l'annulation
    // (ICancellationService), pas du retour.
    // COSMECHIC-BUSINESS-POLICY-001 : RETURN_WINDOW_DAYS=30 approuvé et branché.
    // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 3/4) : décision PM appliquée — la
    // fenêtre de 30 jours et les déclarations d'état (non ouvert/non utilisé/revendable) ne
    // gouvernent QUE ReturnReasonCategory.ChangeOfMind (voir ReturnService.CanRequestReturnAsync).
    // Les autres catégories restent soumises aux portes de base (commande expédiée/livrée,
    // paiement confirmé, quantité disponible) mais jamais à la fenêtre commerciale ni à la
    // restriction "produit ouvert" — sans pour autant devenir "toujours acceptées" : aucune
    // période de garantie légale n'est inventée ici, seule l'absence de fenêtre commerciale
    // est codée. La restriction commerciale ChangeOfMind ne limite jamais un droit légal.
    public interface IReturnService
    {
        // CanRequestReturn centralisée (section 20) : appelée à la fois par la création
        // réelle et exposable telle quelle à une vue pour éviter la duplication de règles.
        // category=null (aperçu GET, avant que le client ait choisi une catégorie par ligne) :
        // seules les portes de base s'appliquent, aucune règle spécifique à une catégorie.
        Task<ReturnEligibility> CanRequestReturnAsync(
            OrderDetail orderDetail,
            int requestedQuantity,
            ReturnReasonCategory? category,
            bool? isOpened,
            bool? isUsed,
            bool? customerDeclaredResellable);

        Task<ReturnRequestResult> CreateReturnRequestAsync(
            int orderId, string requestingUserId, string? reason, string? customerComment, IReadOnlyList<ReturnItemInput> items);

        Task<ReturnActionResult> ApproveAsync(int returnRequestId, string actorUserId, string? adminComment);

        Task<ReturnActionResult> RejectAsync(int returnRequestId, string actorUserId, string? adminComment);

        // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 7) : seule sortie possible de
        // NeedsSafetyReview — admin-only, POST, antiforgery, auditée (voir OrderOperationsController
        // et ReturnPolicyImplementationTests). Ramène la demande dans la file normale
        // (Requested) : ne l'approuve ni ne la rejette elle-même.
        Task<ReturnActionResult> ReleaseSafetyReviewAsync(int returnRequestId, string actorUserId, string? adminComment);

        Task<ReturnActionResult> MarkReceivedAsync(int returnRequestId, string actorUserId);

        Task<ReturnActionResult> CompleteAsync(int returnRequestId, string actorUserId);
    }
}
