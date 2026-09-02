using Cosmechic.Models;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cosmechic.Services
{
    // COSMECHIC-BUSINESS-POLICY-001 (section 3) : RETURN_WINDOW_DAYS=30, approuvé par le PM.
    // CanRequestReturnAsync reste l'unique source de vérité — aucune duplication de la règle
    // ailleurs (contrôleur, vue). CommercePolicyOptions.ReturnWindowDays reste nullable par
    // conception (COSMECHIC-CONTENT-LEGAL-001) : une valeur non configurée désactive la
    // fenêtre plutôt que d'en inventer une, comportement inchangé pour ce cas.
    public class ReturnService(
        CosmechicsContext context, IOrderLifecycleService lifecycleService, IOptions<CommercePolicyOptions> policyOptions) : IReturnService
    {
        private static readonly Dictionary<string, HashSet<string>> StatusTransitions = new()
        {
            [SD.ReturnStatusRequested] = new() { SD.ReturnStatusApproved, SD.ReturnStatusRejected },
            // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 7) : seule sortie possible,
            // ramène vers la file normale — jamais directement vers Approved/Rejected, pour
            // qu'une demande sécurité passe toujours par la même porte Requested qu'une
            // demande ordinaire une fois la revue effectuée.
            [SD.ReturnStatusNeedsSafetyReview] = new() { SD.ReturnStatusRequested },
            [SD.ReturnStatusApproved] = new() { SD.ReturnStatusReceived },
            [SD.ReturnStatusReceived] = new() { SD.ReturnStatusCompleted },
            [SD.ReturnStatusRejected] = new(),
            [SD.ReturnStatusCompleted] = new(),
        };

        // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 4) : décision PM appliquée. La
        // fenêtre commerciale de 30 jours et les déclarations d'état du produit (non ouvert/
        // non utilisé/revendable) ne gouvernent QUE ChangeOfMind. category=null (aperçu GET
        // avant que le client choisisse une catégorie) : seules les portes de base
        // s'appliquent. Pour DefectOrNonConformity/WrongItemOrMerchantFault/
        // SafetyOrAdverseReaction, la fenêtre et les déclarations sont ignorées — mais les
        // portes de base (commande expédiée/livrée, paiement confirmé, quantité disponible)
        // restent obligatoires : l'absence de fenêtre commerciale n'est jamais transformée en
        // "retour toujours accepté", et aucune période de garantie légale n'est inventée ici.
        public async Task<ReturnEligibility> CanRequestReturnAsync(
            OrderDetail orderDetail,
            int requestedQuantity,
            ReturnReasonCategory? category,
            bool? isOpened,
            bool? isUsed,
            bool? customerDeclaredResellable)
        {
            if (requestedQuantity <= 0)
            {
                return new ReturnIneligible("La quantité à retourner doit être positive.");
            }

            var order = orderDetail.OrderHeader ?? await context.OrderHeaders.FirstAsync(o => o.Id == orderDetail.OrderHeaderId);

            // Portes de base (section 19), inconditionnelles quelle que soit la catégorie.
            // Une commande jamais expédiée relève de l'annulation, pas du retour.
            if (order.FulfillmentStatus is not (SD.FulfillmentStatusShipped or SD.FulfillmentStatusDelivered))
            {
                return new ReturnIneligible("Seule une commande expédiée ou livrée peut faire l'objet d'un retour.");
            }

            if (order.PaymentStatus is not (SD.PaymentStatusPaid or SD.PaymentStatusPartiallyRefunded))
            {
                return new ReturnIneligible("Cette commande n'a pas de paiement confirmé à rembourser.");
            }

            if (category == ReturnReasonCategory.ChangeOfMind)
            {
                // COSMECHIC-BUSINESS-POLICY-001 (section 3) : fenêtre de retour de 30 jours,
                // comptée depuis la date de livraison réelle (DeliveredAt) ; si la commande n'a
                // été qu'expédiée sans encore être marquée livrée, depuis la date d'expédition
                // (ShippedAt). Frontière explicite : le jour calendaire 30 complet reste
                // éligible (elapsedCalendarDays > 30 rejette seulement à partir du jour 31),
                // convention "30 jours pleins" la plus favorable au client. Comparaison
                // volontairement basée sur la DATE calendaire (.Date, sans l'heure) plutôt que
                // sur TimeSpan.TotalDays brut — voir ReturnWindowTests.cs.
                var returnWindowDays = policyOptions.Value.ReturnWindowDays;
                if (returnWindowDays.HasValue)
                {
                    var referenceDate = order.DeliveredAt ?? order.ShippedAt;
                    if (referenceDate == null)
                    {
                        return new ReturnIneligible("Aucune date d'expédition ou de livraison enregistrée pour cette commande.");
                    }

                    var elapsedCalendarDays = (DateTime.UtcNow.Date - referenceDate.Value.Date).Days;
                    if (elapsedCalendarDays > returnWindowDays.Value)
                    {
                        return new ReturnIneligible(
                            $"La fenêtre de retour de {returnWindowDays.Value} jours est dépassée ({elapsedCalendarDays} jours écoulés).");
                    }
                }

                // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 3/6) : décision PM —
                // changement d'avis admissible uniquement si le produit est déclaré non
                // ouvert, non utilisé et revendable. Les trois déclarations sont obligatoires
                // (non-null) pour cette catégorie ; une valeur manquante (null) est traitée
                // comme un refus, jamais comme "non applicable" ici (contrairement aux autres
                // catégories, où null signifie réellement non pertinent).
                if (isOpened != false || isUsed != false || customerDeclaredResellable != true)
                {
                    return new ReturnIneligible(
                        "Un retour pour changement d'avis n'est admissible que si le produit est déclaré non ouvert, non utilisé et revendable.");
                }
            }

            // Invariant (section 17) : quantité retournée cumulée <= quantité achetée -
            // quantité déjà réclamée par une autre demande non rejetée (Requested/
            // NeedsSafetyReview/Approved/Received/Completed comptent tous comme "réclamés",
            // pour ne jamais permettre deux demandes concurrentes de dépasser ensemble la
            // quantité achetée).
            var alreadyClaimed = await context.ReturnItems
                .Where(ri => ri.OrderDetailId == orderDetail.Id && ri.ReturnRequest.Status != SD.ReturnStatusRejected)
                .SumAsync(ri => (int?)ri.Quantity) ?? 0;

            if (requestedQuantity > orderDetail.Count - alreadyClaimed)
            {
                return new ReturnIneligible(
                    $"Quantité demandée ({requestedQuantity}) supérieure à la quantité encore retournable ({orderDetail.Count - alreadyClaimed}).");
            }

            return new ReturnEligible();
        }

        public async Task<ReturnRequestResult> CreateReturnRequestAsync(
            int orderId, string requestingUserId, string? reason, string? customerComment, IReadOnlyList<ReturnItemInput> items)
        {
            if (items.Count == 0)
            {
                return new ReturnRequestRejectedByPolicy("Au moins une ligne à retourner est requise.");
            }

            var order = await context.OrderHeaders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null)
            {
                return new ReturnRequestRejectedByPolicy("Commande introuvable.");
            }

            // Ownership (section 18/55) : un client ne peut demander un retour que sur sa
            // propre commande.
            if (order.ApplicationUserId != requestingUserId)
            {
                return new ReturnRequestRejectedByPolicy("Vous ne pouvez retourner que vos propres commandes.");
            }

            var orderDetails = await context.OrderDetails
                .Where(d => d.OrderHeaderId == orderId)
                .Include(d => d.OrderHeader)
                .ToDictionaryAsync(d => d.Id);

            foreach (var item in items)
            {
                // Ownership de la ligne elle-même (section 18) : jamais un OrderDetailId
                // arbitraire non lié à CETTE commande.
                if (!orderDetails.TryGetValue(item.OrderDetailId, out var detail))
                {
                    return new ReturnRequestRejectedByPolicy("Ligne de commande invalide pour cette commande.");
                }

                var eligibility = await CanRequestReturnAsync(
                    detail, item.Quantity, item.Category, item.IsOpened, item.IsUsed, item.CustomerDeclaredResellable);
                if (eligibility is ReturnIneligible ineligible)
                {
                    return new ReturnRequestRejectedByPolicy(ineligible.Reason);
                }
            }

            // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 7) : une demande portant au
            // moins une ligne SafetyOrAdverseReaction naît directement en NeedsSafetyReview,
            // jamais en Requested — elle ne peut donc jamais être approuvée/rejetée/remboursée
            // avant qu'un admin l'ait explicitement libérée (ReleaseSafetyReviewAsync).
            var initialStatus = items.Any(i => i.Category == ReturnReasonCategory.SafetyOrAdverseReaction)
                ? SD.ReturnStatusNeedsSafetyReview
                : SD.ReturnStatusRequested;

            var returnRequest = new ReturnRequest
            {
                OrderId = order.Id,
                ApplicationUserId = requestingUserId,
                Status = initialStatus,
                Reason = reason,
                CustomerComment = customerComment,
                CreatedAt = DateTime.UtcNow,
            };

            foreach (var item in items)
            {
                returnRequest.Items.Add(new ReturnItem
                {
                    OrderDetailId = item.OrderDetailId,
                    Quantity = item.Quantity,
                    Reason = item.Reason,
                    Category = item.Category,
                    IsOpened = item.IsOpened,
                    IsUsed = item.IsUsed,
                    CustomerDeclaredResellable = item.CustomerDeclaredResellable,
                });
            }

            context.ReturnRequests.Add(returnRequest);
            lifecycleService.RecordEvent(order, "ReturnRequested", null, initialStatus, reason, requestingUserId, SD.ActorTypeCustomer);

            await context.SaveChangesAsync();

            return new ReturnRequestCreated(returnRequest.Id);
        }

        // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 7) : réutilise le même
        // TransitionAsync que les autres actions admin (POST + antiforgery + Admin-only déjà
        // imposés au niveau du contrôleur, transition auditée via lifecycleService.RecordEvent
        // au même titre que toute autre transition). Ne fait rien d'autre que ramener la
        // demande vers Requested : elle doit ensuite suivre le même chemin Approve/Reject
        // qu'une demande ordinaire, jamais approuvée/rejetée directement par cette action.
        public Task<ReturnActionResult> ReleaseSafetyReviewAsync(int returnRequestId, string actorUserId, string? adminComment) =>
            TransitionAsync(returnRequestId, SD.ReturnStatusRequested, actorUserId, adminComment, rr => { });

        public Task<ReturnActionResult> ApproveAsync(int returnRequestId, string actorUserId, string? adminComment) =>
            TransitionAsync(returnRequestId, SD.ReturnStatusApproved, actorUserId, adminComment, rr => rr.ApprovedAt = DateTime.UtcNow);

        public Task<ReturnActionResult> RejectAsync(int returnRequestId, string actorUserId, string? adminComment) =>
            TransitionAsync(returnRequestId, SD.ReturnStatusRejected, actorUserId, adminComment, rr => { });

        public Task<ReturnActionResult> MarkReceivedAsync(int returnRequestId, string actorUserId) =>
            TransitionAsync(returnRequestId, SD.ReturnStatusReceived, actorUserId, null, rr => rr.ReceivedAt = DateTime.UtcNow);

        public Task<ReturnActionResult> CompleteAsync(int returnRequestId, string actorUserId) =>
            TransitionAsync(returnRequestId, SD.ReturnStatusCompleted, actorUserId, null, rr => rr.CompletedAt = DateTime.UtcNow);

        private async Task<ReturnActionResult> TransitionAsync(
            int returnRequestId, string newStatus, string actorUserId, string? adminComment, Action<ReturnRequest> applyTimestamp)
        {
            var returnRequest = await context.ReturnRequests
                .Include(rr => rr.Order)
                .FirstOrDefaultAsync(rr => rr.Id == returnRequestId);
            if (returnRequest == null)
            {
                return new ReturnActionRejected("Demande de retour introuvable.");
            }

            if (!StatusTransitions.TryGetValue(returnRequest.Status, out var allowed) || !allowed.Contains(newStatus))
            {
                return new ReturnActionRejected($"Transition refusée : {returnRequest.Status} -> {newStatus} n'est pas autorisée.");
            }

            var previousStatus = returnRequest.Status;
            returnRequest.Status = newStatus;
            applyTimestamp(returnRequest);
            if (adminComment != null)
            {
                returnRequest.AdminComment = adminComment;
            }

            lifecycleService.RecordEvent(
                returnRequest.Order, $"Return{newStatus}", previousStatus, newStatus, adminComment, actorUserId, SD.ActorTypeAdmin);

            await context.SaveChangesAsync();
            return new ReturnActionApplied();
        }
    }
}
