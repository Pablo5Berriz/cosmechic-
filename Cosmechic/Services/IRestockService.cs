namespace Cosmechic.Services
{
    public abstract record RestockResult;
    public sealed record RestockCompleted : RestockResult;
    public sealed record RestockAlreadyDone : RestockResult;
    public sealed record RestockRejected(string Reason) : RestockResult;

    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 38/39/40) : remboursement financier ≠
    // retour physique — la remise en stock est une décision admin explicite et distincte,
    // jamais automatique, liée à la réception effective de l'article retourné (ReturnRequest
    // au moins Received). Idempotent par construction (ReturnItem.Restocked) : une même
    // unité retournée ne peut être remise en stock qu'une seule fois, garanti par retry
    // optimiste sur Produit.RowVersion (même patron que StripeFulfillmentService).
    public interface IRestockService
    {
        Task<RestockResult> CompleteRestockAsync(int returnItemId, string actorUserId);
    }
}
