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

    public sealed record ReturnItemInput(int OrderDetailId, int Quantity, string? Reason);

    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 15-20) : demandes de retour, possiblement
    // partielles. AUCUNE fenêtre de retour (ex. 30 jours) n'est codée en dur — section 19 :
    // TODO_REQUIRES_BUSINESS_CONFIGURATION tant qu'aucune règle métier n'existe. La seule
    // porte technique est que la commande ait été réellement expédiée/livrée : une commande
    // jamais expédiée relève de l'annulation (ICancellationService), pas du retour.
    public interface IReturnService
    {
        // CanRequestReturn centralisée (section 20) : appelée à la fois par la création
        // réelle et exposable telle quelle à une vue pour éviter la duplication de règles.
        Task<ReturnEligibility> CanRequestReturnAsync(OrderDetail orderDetail, int requestedQuantity);

        Task<ReturnRequestResult> CreateReturnRequestAsync(
            int orderId, string requestingUserId, string? reason, string? customerComment, IReadOnlyList<ReturnItemInput> items);

        Task<ReturnActionResult> ApproveAsync(int returnRequestId, string actorUserId, string? adminComment);

        Task<ReturnActionResult> RejectAsync(int returnRequestId, string actorUserId, string? adminComment);

        Task<ReturnActionResult> MarkReceivedAsync(int returnRequestId, string actorUserId);

        Task<ReturnActionResult> CompleteAsync(int returnRequestId, string actorUserId);
    }
}
