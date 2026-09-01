using Cosmechic.Models;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Services
{
    public class CancellationService(
        CosmechicsContext context,
        IOrderLifecycleService lifecycleService,
        IRefundOrchestrationService refundOrchestrationService) : ICancellationService
    {
        public async Task<CancellationResult> CancelOrderAsync(int orderId, string requestingUserId, bool isAdmin, string? reason)
        {
            var order = await context.OrderHeaders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null)
            {
                return new CancellationRejected("Commande introuvable.");
            }

            // Ownership (section 18/55, réutilise SECURITY-001) : un client ne peut annuler
            // que sa propre commande ; Admin peut annuler n'importe laquelle.
            if (!isAdmin && order.ApplicationUserId != requestingUserId)
            {
                return new CancellationRejected("Vous ne pouvez annuler que vos propres commandes.");
            }

            if (order.OrderStatus == SD.OrderStatusCancelled)
            {
                return new CancellationRejected("Cette commande est déjà annulée.");
            }

            if (order.FulfillmentStatus is SD.FulfillmentStatusShipped or SD.FulfillmentStatusDelivered)
            {
                return new CancellationRejected("Cette commande a déjà été expédiée et ne peut plus être annulée ici — voir le processus de retour.");
            }

            if (order.PaymentStatus == SD.PaymentStatusRefunded)
            {
                return new CancellationRejected("Cette commande est déjà entièrement remboursée.");
            }

            var actorType = isAdmin ? SD.ActorTypeAdmin : SD.ActorTypeCustomer;

            var orderStatusResult = lifecycleService.TryTransitionOrderStatus(
                order, SD.OrderStatusCancelled, reason ?? "Annulation demandée.", requestingUserId, actorType);
            if (orderStatusResult is LifecycleTransitionRejected rejected)
            {
                return new CancellationRejected(rejected.Reason);
            }

            // Section 13 : le stock n'a jamais été touché tant que le paiement n'a pas été
            // confirmé (FulfillmentStatus encore Unfulfilled) — aucune remise en stock à
            // faire ici dans tous les cas, seul un fulfillment déjà effectué en a mouvementé.
            if (order.FulfillmentStatus is SD.FulfillmentStatusUnfulfilled or SD.FulfillmentStatusProcessing)
            {
                lifecycleService.TryTransitionFulfillmentStatus(
                    order, SD.FulfillmentStatusCancelled, "Commande annulée.", requestingUserId, actorType);
            }

            await context.SaveChangesAsync();

            // Section 14 : une commande déjà payée (totalement ou partiellement) implique
            // potentiellement un remboursement — jamais un simple changement de statut qui
            // ignorerait la réalité financière. Le solde remboursable restant est remboursé
            // intégralement via le même workflow idempotent/auditable que tout autre
            // remboursement (section 21+).
            var needsRefund = order.PaymentStatus is SD.PaymentStatusPaid or SD.PaymentStatusPartiallyRefunded;
            if (needsRefund)
            {
                var refundableBalance = order.OrderTotal - order.RefundedAmount;
                if (refundableBalance > 0)
                {
                    await refundOrchestrationService.RequestRefundAsync(
                        order.Id, refundableBalance, null, "Remboursement automatique suite à annulation de commande.", requestingUserId, actorType);
                }

                return new CancellationSucceeded(RefundTriggered: true);
            }

            return new CancellationSucceeded(RefundTriggered: false);
        }
    }
}
