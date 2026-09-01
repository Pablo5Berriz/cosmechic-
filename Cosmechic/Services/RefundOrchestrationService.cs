using Cosmechic.Models;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;
using StripeRefund = Stripe.Refund;

namespace Cosmechic.Services
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 25/28/29/34/35) : cœur technique du lot.
    //
    // Frontière DB / Stripe (section 35/36, AUCUNE transaction distribuée) :
    //   1. Réserver le solde (OrderHeader.RefundedAmount += amount) ET insérer le Refund
    //      Pending dans UNE transaction SQL Server locale, protégée par retry optimiste sur
    //      OrderHeader.RowVersion (même patron que StripeFulfillmentService pour Stock) —
    //      c'est CETTE étape qui rend deux demandes concurrentes réellement mutuellement
    //      exclusives (section 34), jamais une simple lecture "if (refundable) then insert".
    //   2. Une fois la réservation committée, appeler Stripe avec RequestOptions.IdempotencyKey
    //      = Refund.IdempotencyKey (généré et persisté AVANT l'appel, jamais après).
    //   3. Finaliser (Succeeded/Failed) dans une écriture séparée, elle-même idempotente
    //      (no-op si déjà finalisé) — appelée soit par le retour synchrone de l'appel Stripe,
    //      soit par ReconcileFromStripeEventAsync (webhook), selon celui qui arrive en
    //      premier. Un échec Stripe libère la réservation (RefundedAmount -= amount) pour
    //      qu'une nouvelle tentative (RetryFailedRefundAsync, MÊME IdempotencyKey) puisse
    //      re-réserver proprement.
    public class RefundOrchestrationService(
        CosmechicsContext context,
        IStripeRefundService stripeRefundService,
        IOrderLifecycleService lifecycleService,
        ILogger<RefundOrchestrationService> logger) : IRefundOrchestrationService
    {
        private const int MaxConcurrencyAttempts = 3;

        public async Task<RefundResult> RequestRefundAsync(
            int orderId, decimal amount, int? returnRequestId, string? reason, string? requestedByUserId, string actorType)
        {
            if (amount <= 0)
            {
                return new RefundRejected("Le montant à rembourser doit être positif.");
            }

            for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
            {
                var order = await context.OrderHeaders.FirstOrDefaultAsync(o => o.Id == orderId);
                if (order == null)
                {
                    return new RefundRejected("Commande introuvable.");
                }

                if (string.IsNullOrEmpty(order.PaymentIntentId))
                {
                    return new RefundRejected("Aucun paiement Stripe confirmé pour cette commande.");
                }

                // Garde du solde remboursable (section 25) : appliquée ici en mémoire, puis
                // réellement rendue infranchissable par le jeton de concurrence RowVersion
                // ci-dessous (section 34) — deux requêtes concurrentes qui, prises isolément,
                // passeraient chacune cette garde ne peuvent pas toutes deux committer.
                var refundableBalance = order.OrderTotal - order.RefundedAmount;
                if (amount > refundableBalance)
                {
                    return new RefundRejected(
                        $"Montant demandé ({amount:0.00}) supérieur au solde remboursable ({refundableBalance:0.00}).");
                }

                order.RefundedAmount += amount;

                var refund = new Cosmechic.Models.Refund
                {
                    OrderId = order.Id,
                    ReturnRequestId = returnRequestId,
                    IdempotencyKey = Guid.NewGuid().ToString("N"),
                    Amount = amount,
                    Status = SD.RefundStatusPending,
                    Reason = reason,
                    RequestedByUserId = requestedByUserId,
                    ActorType = actorType,
                    CreatedAt = DateTime.UtcNow,
                };
                context.Refunds.Add(refund);
                lifecycleService.RecordEvent(order, "RefundRequested", null, SD.RefundStatusPending, reason, requestedByUserId, actorType);

                await using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyAttempts)
                {
                    await transaction.RollbackAsync();
                    logger.LogWarning(
                        "Conflit de concurrence RowVersion à la réservation du remboursement pour la commande {OrderId}, nouvelle tentative ({Attempt}/{Max})",
                        orderId, attempt + 1, MaxConcurrencyAttempts);
                    context.ChangeTracker.Clear();
                    continue;
                }

                // Réservation committée : l'appel Stripe et sa finalisation ne participent
                // plus à la garde de concurrence ci-dessus.
                return await CallStripeAndFinalizeAsync(refund, order);
            }

            return new RefundRejected("Conflit de concurrence répété sur le solde remboursable de la commande.");
        }

        public async Task<RefundResult> RetryFailedRefundAsync(int refundId, string? actorUserId, string actorType)
        {
            for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
            {
                var refund = await context.Refunds.FirstOrDefaultAsync(r => r.Id == refundId);
                if (refund == null)
                {
                    return new RefundRejected("Remboursement introuvable.");
                }

                if (refund.Status != SD.RefundStatusFailed)
                {
                    return new RefundRejected($"Seul un remboursement à l'état Failed peut être retenté (état actuel : {refund.Status}).");
                }

                var order = await context.OrderHeaders.FirstOrDefaultAsync(o => o.Id == refund.OrderId);
                if (order == null)
                {
                    return new RefundRejected("Commande introuvable.");
                }

                var refundableBalance = order.OrderTotal - order.RefundedAmount;
                if (refund.Amount > refundableBalance)
                {
                    return new RefundRejected(
                        $"Solde remboursable insuffisant pour retenter ce remboursement ({refund.Amount:0.00} > {refundableBalance:0.00}).");
                }

                order.RefundedAmount += refund.Amount;
                refund.Status = SD.RefundStatusPending;
                refund.FailureCode = null;
                lifecycleService.RecordEvent(order, "RefundRetried", SD.RefundStatusFailed, SD.RefundStatusPending, null, actorUserId, actorType);

                await using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyAttempts)
                {
                    await transaction.RollbackAsync();
                    context.ChangeTracker.Clear();
                    continue;
                }

                // Même IdempotencyKey que la tentative précédente (section 28/37) : si le
                // premier appel Stripe avait en réalité réussi malgré une erreur perçue côté
                // serveur, Stripe renvoie ce résultat déjà obtenu plutôt que d'en créer un
                // second.
                return await CallStripeAndFinalizeAsync(refund, order);
            }

            return new RefundRejected("Conflit de concurrence répété sur le solde remboursable de la commande.");
        }

        public async Task ReconcileFromStripeEventAsync(string? stripeRefundId, int? refundRecordId, string stripeStatus, string? failureCode)
        {
            Cosmechic.Models.Refund? refund = null;
            if (refundRecordId.HasValue)
            {
                refund = await context.Refunds.FirstOrDefaultAsync(r => r.Id == refundRecordId.Value);
            }

            if (refund == null && !string.IsNullOrEmpty(stripeRefundId))
            {
                refund = await context.Refunds.FirstOrDefaultAsync(r => r.StripeRefundId == stripeRefundId);
            }

            if (refund == null)
            {
                logger.LogWarning(
                    "Webhook Stripe refund : aucun Refund correspondant (RefundRecordId={RefundRecordId}, StripeRefundId={StripeRefundId})",
                    refundRecordId, stripeRefundId);
                return;
            }

            if (refund.Status != SD.RefundStatusPending)
            {
                // Déjà finalisé (chemin synchrone ou événement précédent) — idempotent.
                return;
            }

            var succeeded = string.Equals(stripeStatus, "succeeded", StringComparison.OrdinalIgnoreCase);
            var failed = string.Equals(stripeStatus, "failed", StringComparison.OrdinalIgnoreCase);

            if (succeeded)
            {
                await FinalizeAfterStripeResponseAsync(refund.Id, stripeRefundId ?? refund.StripeRefundId ?? string.Empty, SD.RefundStatusSucceeded);
            }
            else if (failed)
            {
                await ReleaseReservationAndMarkFailedAsync(refund.Id, failureCode ?? "stripe_failed", stripeRefundId);
            }
            // Sinon (ex. "pending" côté Stripe) : rien à faire, un événement ultérieur
            // (charge.refunded ou refund.updated) finalisera.
        }

        public async Task<RefundWebhookOutcome> ProcessRefundEventAsync(
            string stripeEventId, string eventType, string? stripeRefundId, int? refundRecordId, string stripeStatus, string? failureCode)
        {
            // Barrière n°1 (chemin rapide), identique à StripeFulfillmentService.
            if (await context.ProcessedStripeEvents.AnyAsync(e => e.StripeEventId == stripeEventId))
            {
                logger.LogInformation("Stripe refund event {EventId} already processed, skipping", stripeEventId);
                return RefundWebhookOutcome.AlreadyProcessed;
            }

            var processedEvent = new ProcessedStripeEvent
            {
                StripeEventId = stripeEventId,
                EventType = eventType,
                ReceivedAt = DateTime.UtcNow,
                ProcessingStatus = "Received",
            };
            context.ProcessedStripeEvents.Add(processedEvent);

            try
            {
                // Barrière n°2 (contrainte UNIQUE), identique à StripeFulfillmentService.
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueConstraintViolation(ex))
            {
                context.Entry(processedEvent).State = EntityState.Detached;
                logger.LogInformation("Stripe refund event {EventId} already processed (contrainte UNIQUE), skipping", stripeEventId);
                return RefundWebhookOutcome.AlreadyProcessed;
            }

            await ReconcileFromStripeEventAsync(stripeRefundId, refundRecordId, stripeStatus, failureCode);

            processedEvent.ProcessingStatus = "Processed";
            processedEvent.ProcessedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();

            return RefundWebhookOutcome.Processed;
        }

        private async Task<RefundResult> CallStripeAndFinalizeAsync(Cosmechic.Models.Refund refund, OrderHeader order)
        {
            StripeRefund? stripeRefund = null;
            Exception? stripeException = null;
            try
            {
                var options = new RefundCreateOptions
                {
                    PaymentIntent = order.PaymentIntentId,
                    Amount = (long)Math.Round(refund.Amount * 100, MidpointRounding.AwayFromZero),
                    Metadata = new Dictionary<string, string> { ["RefundRecordId"] = refund.Id.ToString() },
                };
                var requestOptions = new RequestOptions { IdempotencyKey = refund.IdempotencyKey };
                stripeRefund = stripeRefundService.CreateRefund(options, requestOptions);
            }
            catch (Exception ex)
            {
                stripeException = ex;
            }

            if (stripeException != null || stripeRefund == null)
            {
                logger.LogError(stripeException, "Échec de l'appel Stripe Refund pour Refund {RefundId} (commande {OrderId})", refund.Id, order.Id);
                await ReleaseReservationAndMarkFailedAsync(refund.Id, stripeException?.Message ?? "no_response", null);
                return new RefundFailed(refund.Id, "L'appel Stripe a échoué. Solde libéré, une nouvelle tentative est possible.");
            }

            if (string.Equals(stripeRefund.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                await ReleaseReservationAndMarkFailedAsync(refund.Id, stripeRefund.FailureReason ?? "stripe_failed", stripeRefund.Id);
                return new RefundFailed(refund.Id, "Stripe a refusé le remboursement.");
            }

            var succeeded = string.Equals(stripeRefund.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
            await FinalizeAfterStripeResponseAsync(refund.Id, stripeRefund.Id, succeeded ? SD.RefundStatusSucceeded : SD.RefundStatusPending);

            return succeeded
                ? new RefundSucceeded(refund.Id, stripeRefund.Id)
                : new RefundPendingStripeConfirmation(refund.Id);
        }

        // Idempotent (section 37) : no-op si le Refund a déjà été finalisé par une autre
        // voie (le webhook peut arriver avant que cette méthode ne s'exécute).
        private async Task FinalizeAfterStripeResponseAsync(int refundId, string stripeRefundId, string newStatus)
        {
            for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
            {
                var refund = await context.Refunds.FirstAsync(r => r.Id == refundId);
                if (refund.Status != SD.RefundStatusPending)
                {
                    return;
                }

                refund.StripeRefundId = stripeRefundId;
                refund.Status = newStatus;
                if (newStatus == SD.RefundStatusSucceeded)
                {
                    refund.CompletedAt = DateTime.UtcNow;
                }

                if (newStatus == SD.RefundStatusSucceeded)
                {
                    var order = await context.OrderHeaders.FirstAsync(o => o.Id == refund.OrderId);
                    var newPaymentStatus = order.RefundedAmount >= order.OrderTotal
                        ? SD.PaymentStatusRefunded
                        : SD.PaymentStatusPartiallyRefunded;
                    lifecycleService.TryTransitionPaymentStatus(
                        order, newPaymentStatus, $"Remboursement #{refund.Id} confirmé par Stripe.", refund.RequestedByUserId, refund.ActorType);
                    lifecycleService.RecordEvent(order, "RefundSucceeded", SD.RefundStatusPending, SD.RefundStatusSucceeded, null, refund.RequestedByUserId, refund.ActorType);
                }

                try
                {
                    await context.SaveChangesAsync();
                    return;
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyAttempts)
                {
                    context.ChangeTracker.Clear();
                }
            }

            logger.LogError("Conflit de concurrence répété à la finalisation du remboursement {RefundId}", refundId);
        }

        // Idempotent (section 37) : no-op si déjà finalisé.
        private async Task ReleaseReservationAndMarkFailedAsync(int refundId, string failureCode, string? stripeRefundId)
        {
            for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
            {
                var refund = await context.Refunds.FirstAsync(r => r.Id == refundId);
                if (refund.Status != SD.RefundStatusPending)
                {
                    return;
                }

                var order = await context.OrderHeaders.FirstAsync(o => o.Id == refund.OrderId);
                order.RefundedAmount -= refund.Amount;
                refund.Status = SD.RefundStatusFailed;
                refund.FailureCode = failureCode;
                if (stripeRefundId != null)
                {
                    refund.StripeRefundId = stripeRefundId;
                }

                lifecycleService.RecordEvent(order, "RefundFailed", SD.RefundStatusPending, SD.RefundStatusFailed, failureCode, refund.RequestedByUserId, refund.ActorType);

                try
                {
                    await context.SaveChangesAsync();
                    return;
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyAttempts)
                {
                    context.ChangeTracker.Clear();
                }
            }

            logger.LogError("Conflit de concurrence répété à la libération de la réservation du remboursement {RefundId}", refundId);
        }
    }
}
