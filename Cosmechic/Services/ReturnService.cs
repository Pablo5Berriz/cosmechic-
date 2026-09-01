using Cosmechic.Models;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Services
{
    public class ReturnService(CosmechicsContext context, IOrderLifecycleService lifecycleService) : IReturnService
    {
        private static readonly Dictionary<string, HashSet<string>> StatusTransitions = new()
        {
            [SD.ReturnStatusRequested] = new() { SD.ReturnStatusApproved, SD.ReturnStatusRejected },
            [SD.ReturnStatusApproved] = new() { SD.ReturnStatusReceived },
            [SD.ReturnStatusReceived] = new() { SD.ReturnStatusCompleted },
            [SD.ReturnStatusRejected] = new(),
            [SD.ReturnStatusCompleted] = new(),
        };

        public async Task<ReturnEligibility> CanRequestReturnAsync(OrderDetail orderDetail, int requestedQuantity)
        {
            if (requestedQuantity <= 0)
            {
                return new ReturnIneligible("La quantité à retourner doit être positive.");
            }

            var order = orderDetail.OrderHeader ?? await context.OrderHeaders.FirstAsync(o => o.Id == orderDetail.OrderHeaderId);

            // Porte technique minimale (section 19) : jamais de fenêtre de jours inventée.
            // Une commande jamais expédiée relève de l'annulation, pas du retour.
            if (order.FulfillmentStatus is not (SD.FulfillmentStatusShipped or SD.FulfillmentStatusDelivered))
            {
                return new ReturnIneligible("Seule une commande expédiée ou livrée peut faire l'objet d'un retour.");
            }

            if (order.PaymentStatus is not (SD.PaymentStatusPaid or SD.PaymentStatusPartiallyRefunded))
            {
                return new ReturnIneligible("Cette commande n'a pas de paiement confirmé à rembourser.");
            }

            // Invariant (section 17) : quantité retournée cumulée <= quantité achetée -
            // quantité déjà réclamée par une autre demande non rejetée (Requested/Approved/
            // Received/Completed comptent tous comme "réclamés", pour ne jamais permettre
            // deux demandes concurrentes de dépasser ensemble la quantité achetée).
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

                var eligibility = await CanRequestReturnAsync(detail, item.Quantity);
                if (eligibility is ReturnIneligible ineligible)
                {
                    return new ReturnRequestRejectedByPolicy(ineligible.Reason);
                }
            }

            var returnRequest = new ReturnRequest
            {
                OrderId = order.Id,
                ApplicationUserId = requestingUserId,
                Status = SD.ReturnStatusRequested,
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
                });
            }

            context.ReturnRequests.Add(returnRequest);
            lifecycleService.RecordEvent(order, "ReturnRequested", null, SD.ReturnStatusRequested, reason, requestingUserId, SD.ActorTypeCustomer);

            await context.SaveChangesAsync();

            return new ReturnRequestCreated(returnRequest.Id);
        }

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
