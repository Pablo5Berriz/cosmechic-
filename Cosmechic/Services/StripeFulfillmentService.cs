using Cosmechic.Models;
using Cosmechic.Utility;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe.Checkout;

namespace Cosmechic.Services
{
    // COSMECHIC-ECOM-CORE-001 : cœur transactionnel du lot. Convertit un événement Stripe
    // checkout.session.* déjà authentifié (signature vérifiée par l'appelant) en effet
    // métier unique, sans jamais faire confiance au navigateur ni retraiter un événement
    // ou une commande déjà traités.
    public class StripeFulfillmentService(
        CosmechicsContext context,
        ILogger<StripeFulfillmentService> logger) : IStripeFulfillmentService
    {
        private const int MaxConcurrencyAttempts = 3;

        public async Task<FulfillmentResult> ProcessCheckoutSessionEventAsync(string stripeEventId, string eventType, Session session)
        {
            // Barrière n°1 (idempotence, section 15) — chemin rapide, agnostique du
            // fournisseur : couvre l'immense majorité des livraisons dupliquées
            // consécutives de Stripe (retry après un 5xx transitoire, etc.).
            if (await context.ProcessedStripeEvents.AnyAsync(e => e.StripeEventId == stripeEventId))
            {
                logger.LogInformation("Stripe event {EventId} already processed, skipping", stripeEventId);
                return new FulfillmentResult(FulfillmentOutcome.AlreadyProcessed);
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
                // Garantie réelle contre la concurrence (section 16) : deux requêtes
                // webhook concurrentes peuvent toutes deux dépasser le AnyAsync ci-dessus
                // avant qu'aucune n'ait committé. Seule la contrainte UNIQUE en base
                // tranche laquelle des deux gagne — la seconde échoue ici, pas avant.
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                context.Entry(processedEvent).State = EntityState.Detached;
                logger.LogInformation(
                    "Stripe event {EventId} already processed (contrainte UNIQUE, concurrence détectée), skipping",
                    stripeEventId);
                return new FulfillmentResult(FulfillmentOutcome.AlreadyProcessed);
            }

            return await ProcessOwnedEventAsync(processedEvent, eventType, session);
        }

        private async Task<FulfillmentResult> ProcessOwnedEventAsync(
            ProcessedStripeEvent processedEvent, string eventType, Session session)
        {
            if (session.Metadata is null
                || !session.Metadata.TryGetValue("OrderId", out var orderIdRaw)
                || !int.TryParse(orderIdRaw, out var orderId))
            {
                logger.LogWarning("Stripe event {EventId} sans OrderId exploitable en metadata", processedEvent.StripeEventId);
                await MarkEventAsync(processedEvent, "Failed_NoOrderId", null);
                return new FulfillmentResult(FulfillmentOutcome.OrderNotFound, "OrderId manquant dans les metadata Stripe.");
            }

            var orderHeader = await context.OrderHeaders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (orderHeader == null || orderHeader.SessionId != session.Id)
            {
                logger.LogWarning(
                    "Stripe event {EventId} référence la commande {OrderId}, introuvable ou dont le SessionId ne correspond pas",
                    processedEvent.StripeEventId, orderId);
                await MarkEventAsync(processedEvent, "Failed_OrderMismatch", orderHeader?.Id);
                return new FulfillmentResult(FulfillmentOutcome.OrderNotFound, "Commande introuvable ou SessionId incohérent.");
            }

            processedEvent.OrderId = orderHeader.Id;

            // Barrière n°2 (section 23) : une commande déjà payée ne peut jamais être
            // fulfillée une seconde fois, même par un StripeEventId différent pour la
            // même Session/Order (ex. checkout.session.completed puis
            // async_payment_succeeded pour le même paiement).
            if (orderHeader.PaymentStatus == SD.PaymentStatusApproved)
            {
                logger.LogInformation(
                    "Commande {OrderId} déjà payée, événement {EventId} ignoré (seconde barrière anti-doublon)",
                    orderHeader.Id, processedEvent.StripeEventId);
                await MarkEventAsync(processedEvent, "Skipped_AlreadyFulfilled", orderHeader.Id);
                return new FulfillmentResult(FulfillmentOutcome.AlreadyProcessed);
            }

            var isFailureEvent = eventType == "checkout.session.async_payment_failed";
            var isPaid = string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase);

            if (isFailureEvent || !isPaid)
            {
                orderHeader.PaymentStatus = SD.PaymentStatusRejected;
                orderHeader.OrderStatus = SD.StatusCancelled;
                await MarkEventAsync(processedEvent, "Processed_PaymentFailed", orderHeader.Id);
                logger.LogInformation(
                    "Paiement échoué/annulé pour la commande {OrderId} (événement {EventType})", orderHeader.Id, eventType);
                return new FulfillmentResult(FulfillmentOutcome.PaymentFailed);
            }

            // Validation montant/devise (section 17) : le montant de la commande vient du
            // serveur (OrderTotal calculé par CheckoutService au moment de la création de
            // la session) ; Stripe ne fait ici que confirmer qu'il correspond bien à ce
            // qui a été effectivement demandé — jamais l'inverse.
            var expectedAmount = (long)Math.Round(orderHeader.OrderTotal * 100, MidpointRounding.AwayFromZero);
            if (session.AmountTotal != expectedAmount)
            {
                logger.LogError(
                    "Montant Stripe {StripeAmount} != montant attendu {ExpectedAmount} pour la commande {OrderId}",
                    session.AmountTotal, expectedAmount, orderHeader.Id);
                await MarkEventAsync(processedEvent, "Failed_AmountMismatch", orderHeader.Id);
                return new FulfillmentResult(FulfillmentOutcome.AmountMismatch, "Montant Stripe incohérent avec la commande.");
            }

            if (!string.Equals(session.Currency, CheckoutConstants.Currency, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError(
                    "Devise Stripe {StripeCurrency} != devise attendue {ExpectedCurrency} pour la commande {OrderId}",
                    session.Currency, CheckoutConstants.Currency, orderHeader.Id);
                await MarkEventAsync(processedEvent, "Failed_CurrencyMismatch", orderHeader.Id);
                return new FulfillmentResult(FulfillmentOutcome.CurrencyMismatch, "Devise Stripe incohérente avec la commande.");
            }

            return await FulfillWithStockConcurrencyAsync(processedEvent, orderHeader, session);
        }

        // Fulfillment transactionnel (section 18) avec retry borné sur conflit de
        // concurrence optimiste RowVersion (section 19) : deux fulfillments concurrents
        // sur le même produit ne peuvent jamais faire passer Stock sous zéro — l'un des
        // deux gagne, l'autre relit l'état réel et retente contre les valeurs à jour.
        private async Task<FulfillmentResult> FulfillWithStockConcurrencyAsync(
            ProcessedStripeEvent processedEvent, OrderHeader orderHeader, Session session)
        {
            var orderDetails = await context.OrderDetails
                .Where(d => d.OrderHeaderId == orderHeader.Id)
                .Include(d => d.Produit)
                .ToListAsync();

            for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
            {
                if (attempt > 1)
                {
                    foreach (var detail in orderDetails)
                    {
                        await context.Entry(detail.Produit).ReloadAsync();
                    }
                }

                var insufficient = orderDetails.FirstOrDefault(d => d.Produit.Stock < d.Count);
                if (insufficient != null)
                {
                    // Cas difficile documenté (section 20) : le paiement Stripe a
                    // réellement eu lieu (isPaid == true a déjà été vérifié) mais le stock
                    // a été consommé entre-temps par une autre commande. On ne cache pas
                    // le problème et on n'invente pas de système de réservation : le
                    // paiement est reconnu comme réellement effectué (PaymentStatus =
                    // Approved), mais OrderStatus reste Pending au lieu de passer à
                    // Processing — cette combinaison (payé + toujours Pending) EST le
                    // signal explicite qu'une remédiation administrative (remboursement
                    // ou réapprovisionnement) est nécessaire.
                    orderHeader.PaymentStatus = SD.PaymentStatusApproved;
                    orderHeader.PaymentIntentId = session.PaymentIntentId;
                    orderHeader.PaymentDate = DateTime.UtcNow;
                    await MarkEventAsync(processedEvent, "Processed_StockUnavailable", orderHeader.Id);
                    logger.LogError(
                        "Stock insuffisant pour le produit {ProduitId} au fulfillment de la commande {OrderId} malgré un paiement confirmé — remédiation manuelle requise",
                        insufficient.ProduitId, orderHeader.Id);
                    return new FulfillmentResult(
                        FulfillmentOutcome.StockUnavailable, $"Stock insuffisant pour le produit {insufficient.ProduitId}.");
                }

                foreach (var detail in orderDetails)
                {
                    detail.Produit.Stock -= detail.Count;
                    if (string.IsNullOrEmpty(detail.ProduitNom))
                    {
                        // Snapshot historique préparé par COSMECHIC-DATA-001, raccordé ici :
                        // le nom du produit au moment de l'achat survit à un renommage
                        // ultérieur du produit vivant.
                        detail.ProduitNom = detail.Produit.Nom;
                    }
                }

                orderHeader.PaymentStatus = SD.PaymentStatusApproved;
                orderHeader.OrderStatus = SD.StatusInProcess;
                orderHeader.PaymentIntentId = session.PaymentIntentId;
                orderHeader.PaymentDate = DateTime.UtcNow;

                // Nettoyage du panier uniquement après fulfillment vérifié (section 25),
                // jamais au simple affichage de la page de confirmation.
                var cartItems = await context.ShoppingCarts
                    .Where(c => c.ApplicationUserId == orderHeader.ApplicationUserId)
                    .ToListAsync();
                context.ShoppingCarts.RemoveRange(cartItems);

                processedEvent.ProcessingStatus = "Processed";
                processedEvent.ProcessedAt = DateTime.UtcNow;

                await using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    logger.LogInformation(
                        "Commande {OrderId} fulfillie avec succès (événement {EventId}, tentative {Attempt})",
                        orderHeader.Id, processedEvent.StripeEventId, attempt);
                    return new FulfillmentResult(FulfillmentOutcome.Fulfilled);
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyAttempts)
                {
                    await transaction.RollbackAsync();
                    logger.LogWarning(
                        "Conflit de concurrence RowVersion au fulfillment de la commande {OrderId}, nouvelle tentative ({Attempt}/{Max})",
                        orderHeader.Id, attempt + 1, MaxConcurrencyAttempts);
                }
            }

            // Tentatives épuisées : traité comme le même cas de remédiation documenté
            // ci-dessus plutôt que de laisser une exception non gérée remonter au
            // webhook (qui provoquerait un retry Stripe inutile sur un problème qui ne
            // se résoudra pas tout seul).
            orderHeader.PaymentStatus = SD.PaymentStatusApproved;
            orderHeader.PaymentIntentId = session.PaymentIntentId;
            orderHeader.PaymentDate = DateTime.UtcNow;
            await MarkEventAsync(processedEvent, "Processed_ConcurrencyExhausted", orderHeader.Id);
            logger.LogError(
                "Conflits de concurrence répétés au fulfillment de la commande {OrderId} — remédiation manuelle requise",
                orderHeader.Id);
            return new FulfillmentResult(FulfillmentOutcome.StockUnavailable, "Conflit de concurrence répété.");
        }

        private async Task MarkEventAsync(ProcessedStripeEvent processedEvent, string status, int? orderId)
        {
            processedEvent.ProcessingStatus = status;
            processedEvent.ProcessedAt = DateTime.UtcNow;
            processedEvent.OrderId = orderId;
            await context.SaveChangesAsync();
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            // SQL Server : 2627 = violation de contrainte UNIQUE/PK, 2601 = index unique
            // dupliqué. C'est le fournisseur réellement utilisé en production et par les
            // tests d'intégration SQL Server de ce lot (InMemory ne reproduit pas cette
            // erreur — voir COSMECHIC-ECOM-CORE-001.md).
            return ex.InnerException is SqlException sqlEx && sqlEx.Number is 2627 or 2601;
        }
    }
}
