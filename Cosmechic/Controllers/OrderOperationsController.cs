using System.Security.Claims;
using Cosmechic.Models;
using Cosmechic.Models.ViewModels;
using Cosmechic.Services;
using Cosmechic.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Controllers
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 44/45/52) : surface admin minimale pour
    // les opérations post-achat. Toutes les actions sont POST + antiforgery + Admin-only ;
    // aucune n'accepte une entité métier complète (section 54) — chacune délègue à un
    // service dédié qui est la seule autorité pour la transition/mutation demandée.
    [Authorize(Roles = "Admin")]
    public class OrderOperationsController(
        CosmechicsContext context,
        IOrderLifecycleService lifecycleService,
        ICancellationService cancellationService,
        IReturnService returnService,
        IRefundOrchestrationService refundOrchestrationService,
        IRestockService restockService) : Controller
    {
        public async Task<IActionResult> Details(int id)
        {
            var order = await context.OrderHeaders
                .Include(o => o.ApplicationUser)
                .Include(o => o.OrderDetails)
                .Include(o => o.StatusHistory)
                .Include(o => o.ReturnRequests).ThenInclude(rr => rr.Items).ThenInclude(ri => ri.OrderDetail)
                .Include(o => o.Refunds)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkShipped(MarkShippedInput input)
        {
            var order = await context.OrderHeaders.FirstOrDefaultAsync(o => o.Id == input.OrderId);
            if (order == null)
            {
                return NotFound();
            }

            var result = lifecycleService.TryTransitionFulfillmentStatus(
                order, SD.FulfillmentStatusShipped, "Commande expédiée.", GetCurrentUserId(), SD.ActorTypeAdmin);

            if (result is LifecycleTransitionRejected rejected)
            {
                TempData["error"] = rejected.Reason;
                return RedirectToAction(nameof(Details), new { id = input.OrderId });
            }

            order.TrackingNumber = string.IsNullOrWhiteSpace(input.TrackingNumber) ? null : input.TrackingNumber.Trim();
            order.Carrier = string.IsNullOrWhiteSpace(input.Carrier) ? null : input.Carrier.Trim();
            order.ShippedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = input.OrderId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkDelivered(int orderId)
        {
            var order = await context.OrderHeaders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null)
            {
                return NotFound();
            }

            var result = lifecycleService.TryTransitionFulfillmentStatus(
                order, SD.FulfillmentStatusDelivered, "Commande livrée.", GetCurrentUserId(), SD.ActorTypeAdmin);

            if (result is LifecycleTransitionRejected rejected)
            {
                TempData["error"] = rejected.Reason;
                return RedirectToAction(nameof(Details), new { id = orderId });
            }

            order.DeliveredAt = DateTime.UtcNow;

            // Section 7 : une commande livrée et intégralement réglée est considérée
            // terminée du point de vue du cycle de vie global (OrderStatus), distinct du
            // suivi d'expédition (FulfillmentStatus) qui, lui, l'était déjà à "Delivered".
            lifecycleService.TryTransitionOrderStatus(
                order, SD.OrderStatusCompleted, "Commande livrée.", GetCurrentUserId(), SD.ActorTypeAdmin);

            await context.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(OrderIdReasonInput input)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var result = await cancellationService.CancelOrderAsync(input.OrderId, userId, isAdmin: true, input.Reason);
            if (result is CancellationRejected rejected)
            {
                TempData["error"] = rejected.Reason;
            }

            return RedirectToAction(nameof(Details), new { id = input.OrderId });
        }

        // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 7) : seule sortie possible de
        // NeedsSafetyReview — admin-only (hérité du contrôleur), POST, antiforgery, auditée
        // (ReturnService.ReleaseSafetyReviewAsync -> lifecycleService.RecordEvent). Ramène la
        // demande en Requested, ne l'approuve ni ne la rejette elle-même.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReleaseSafetyReview(ReturnRequestIdCommentInput input)
        {
            var result = await returnService.ReleaseSafetyReviewAsync(input.ReturnRequestId, GetCurrentUserId()!, input.Comment);
            return await RedirectAfterReturnActionAsync(input.ReturnRequestId, result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveReturn(ReturnRequestIdCommentInput input)
        {
            var result = await returnService.ApproveAsync(input.ReturnRequestId, GetCurrentUserId()!, input.Comment);
            return await RedirectAfterReturnActionAsync(input.ReturnRequestId, result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectReturn(ReturnRequestIdCommentInput input)
        {
            var result = await returnService.RejectAsync(input.ReturnRequestId, GetCurrentUserId()!, input.Comment);
            return await RedirectAfterReturnActionAsync(input.ReturnRequestId, result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkReturnReceived(int returnRequestId)
        {
            var result = await returnService.MarkReceivedAsync(returnRequestId, GetCurrentUserId()!);
            return await RedirectAfterReturnActionAsync(returnRequestId, result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteReturn(int returnRequestId)
        {
            var result = await returnService.CompleteAsync(returnRequestId, GetCurrentUserId()!);
            return await RedirectAfterReturnActionAsync(returnRequestId, result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TriggerRefund(TriggerRefundInput input)
        {
            var result = await refundOrchestrationService.RequestRefundAsync(
                input.OrderId, input.Amount, input.ReturnRequestId, input.Reason, GetCurrentUserId(), SD.ActorTypeAdmin);

            TempData[result is RefundRejected ? "error" : "success"] = result switch
            {
                RefundRejected r => r.Reason,
                RefundSucceeded => "Remboursement effectué avec succès.",
                RefundFailed f => f.Reason,
                RefundPendingStripeConfirmation => "Remboursement initié, en attente de confirmation Stripe.",
                _ => null,
            };

            return RedirectToAction(nameof(Details), new { id = input.OrderId });
        }

        // COSMECHIC-BUSINESS-POLICY-001 (section 4) : contrairement à TriggerRefund
        // ci-dessus (montant admin ad hoc, inchangé), cette action ne reçoit jamais de
        // montant depuis le navigateur.
        // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 8) : Cause n'est plus une
        // décision confiée à l'admin ici — elle est dérivée côté serveur par
        // RequestReturnRefundAsync à partir des ReturnReasonCategory déjà persistées sur les
        // lignes du retour. Le calcul réel (marchandise/livraison/taxe/cause) est donc
        // entièrement serveur, sans aucune entrée navigateur additionnelle.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TriggerReturnRefund(TriggerReturnRefundInput input)
        {
            var result = await refundOrchestrationService.RequestReturnRefundAsync(
                input.ReturnRequestId, input.Reason, GetCurrentUserId(), SD.ActorTypeAdmin);

            TempData[result is RefundRejected ? "error" : "success"] = result switch
            {
                RefundRejected r => r.Reason,
                RefundSucceeded => "Remboursement du retour effectué avec succès.",
                RefundFailed f => f.Reason,
                RefundPendingStripeConfirmation => "Remboursement du retour initié, en attente de confirmation Stripe.",
                _ => null,
            };

            var returnRequest = await context.ReturnRequests.FirstOrDefaultAsync(rr => rr.Id == input.ReturnRequestId);
            return RedirectToAction(nameof(Details), new { id = returnRequest?.OrderId ?? 0 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RetryRefund(RefundIdInput input)
        {
            var refund = await context.Refunds.FirstOrDefaultAsync(r => r.Id == input.RefundId);
            if (refund == null)
            {
                return NotFound();
            }

            var result = await refundOrchestrationService.RetryFailedRefundAsync(input.RefundId, GetCurrentUserId(), SD.ActorTypeAdmin);
            TempData[result is RefundRejected ? "error" : "success"] = result switch
            {
                RefundRejected r => r.Reason,
                RefundSucceeded => "Nouvelle tentative de remboursement réussie.",
                RefundFailed f => f.Reason,
                RefundPendingStripeConfirmation => "Remboursement initié, en attente de confirmation Stripe.",
                _ => null,
            };

            return RedirectToAction(nameof(Details), new { id = refund.OrderId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteRestock(ReturnItemIdInput input)
        {
            var returnItem = await context.ReturnItems.FirstOrDefaultAsync(ri => ri.Id == input.ReturnItemId);
            if (returnItem == null)
            {
                return NotFound();
            }

            var result = await restockService.CompleteRestockAsync(input.ReturnItemId, GetCurrentUserId()!);
            if (result is RestockRejected rejected)
            {
                TempData["error"] = rejected.Reason;
            }

            var returnRequest = await context.ReturnRequests.FirstAsync(rr => rr.Id == returnItem.ReturnRequestId);
            return RedirectToAction(nameof(Details), new { id = returnRequest.OrderId });
        }

        private async Task<IActionResult> RedirectAfterReturnActionAsync(int returnRequestId, ReturnActionResult result)
        {
            if (result is ReturnActionRejected rejected)
            {
                TempData["error"] = rejected.Reason;
            }

            var returnRequest = await context.ReturnRequests.FirstOrDefaultAsync(rr => rr.Id == returnRequestId);
            return RedirectToAction(nameof(Details), new { id = returnRequest?.OrderId ?? 0 });
        }

        private string? GetCurrentUserId() => (User.Identity as ClaimsIdentity)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
