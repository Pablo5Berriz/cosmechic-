using Cosmechic.Services;
using Cosmechic.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Cosmechic.Controllers
{
    // COSMECHIC-ECOM-CORE-001 (section 12) : seul point d'entrée serveur-à-serveur pour
    // les événements Stripe. Jamais appelé par le navigateur — la page de confirmation
    // (CartController.OrderConfirmation) est désormais une vue d'état pure qui ne fait
    // plus aucune mutation. Aucune authentification cookie ici : la garantie de sécurité
    // vient exclusivement de la vérification de signature HMAC (Stripe-Signature),
    // jamais d'un [Authorize] applicatif — Stripe n'est pas un utilisateur Cosmechic.
    [ApiController]
    [AllowAnonymous]
    [Route("webhooks/stripe")]
    public class StripeWebhookController(
        IStripeFulfillmentService fulfillmentService,
        IRefundOrchestrationService refundOrchestrationService,
        IOptions<StripeSettings> stripeSettings,
        ILogger<StripeWebhookController> logger) : ControllerBase
    {
        private static readonly HashSet<string> CheckoutEventTypes = new()
        {
            "checkout.session.completed",
            "checkout.session.async_payment_succeeded",
            "checkout.session.async_payment_failed",
        };

        // COSMECHIC-COMMERCE-OPERATIONS-001B (section 31) : seul refund.updated est
        // nécessaire — porte directement l'objet Refund (Id/Status/Metadata), permettant une
        // réconciliation par Refund.Id précise, contrairement à charge.refunded (agrégat au
        // niveau de la charge). Ne supporte que ce qui est réellement nécessaire (section 31).
        private const string RefundUpdatedEventType = "refund.updated";

        [HttpPost]
        public async Task<IActionResult> Handle()
        {
            string json;
            using (var reader = new StreamReader(Request.Body))
            {
                json = await reader.ReadToEndAsync();
            }

            var webhookSecret = stripeSettings.Value.WebhookSecret;
            if (string.IsNullOrEmpty(webhookSecret))
            {
                // Secret non configuré : refuser plutôt que d'accepter un webhook non
                // vérifiable (jamais de secret en dur dans le code, section 12).
                logger.LogError("Webhook Stripe reçu mais aucun Stripe:WebhookSecret n'est configuré");
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            Event stripeEvent;
            try
            {
                // throwOnApiVersionMismatch: false — la version d'API du compte Stripe
                // (configurée côté Stripe, indépendante de cette application) n'a aucune
                // raison de coïncider avec celle intégrée au SDK Stripe.net installé ; en
                // exiger la stricte égalité rejetterait des webhooks Stripe parfaitement
                // légitimes. La vérification de signature reste, elle, obligatoire.
                stripeEvent = EventUtility.ConstructEvent(
                    json, Request.Headers["Stripe-Signature"], webhookSecret, throwOnApiVersionMismatch: false);
            }
            catch (StripeException ex)
            {
                // Signature invalide (section 14) : ne doit JAMAIS muter order/payment/
                // stock ni créer un ProcessedStripeEvent réussi. Ne jamais loguer le
                // payload brut ou le secret — seul le message d'erreur Stripe est utile.
                logger.LogWarning("Webhook Stripe rejeté : signature invalide ({Message})", ex.Message);
                return BadRequest();
            }

            logger.LogInformation("Webhook Stripe reçu : {EventId} ({EventType})", stripeEvent.Id, stripeEvent.Type);

            if (stripeEvent.Type == RefundUpdatedEventType)
            {
                return await HandleRefundEventAsync(stripeEvent);
            }

            if (!CheckoutEventTypes.Contains(stripeEvent.Type))
            {
                // Événement valide mais non pertinent pour ce flux : accusé réception
                // sans erreur (section 30), aucun traitement.
                logger.LogInformation("Webhook Stripe {EventId} de type {EventType} non supporté, ignoré", stripeEvent.Id, stripeEvent.Type);
                return Ok();
            }

            if (stripeEvent.Data.Object is not Session session)
            {
                logger.LogWarning("Webhook Stripe {EventId} de type {EventType} sans objet Session exploitable", stripeEvent.Id, stripeEvent.Type);
                return Ok();
            }

            var result = await fulfillmentService.ProcessCheckoutSessionEventAsync(stripeEvent.Id, stripeEvent.Type, session);

            // Toutes les issues métier (idempotence, commande introuvable, montant/devise
            // incohérents, stock indisponible) sont des décisions définitives déjà
            // enregistrées par le service — les rejouer ne changerait rien : 200 pour
            // éviter des retries Stripe inutiles (section 30). Seule une exception non
            // gérée (transitoire) doit provoquer un retry, via un 500 laissé remonter.
            switch (result.Outcome)
            {
                case FulfillmentOutcome.Fulfilled:
                    logger.LogInformation("Webhook Stripe {EventId} : fulfillment réussi", stripeEvent.Id);
                    break;
                case FulfillmentOutcome.AlreadyProcessed:
                    logger.LogInformation("Webhook Stripe {EventId} : déjà traité", stripeEvent.Id);
                    break;
                case FulfillmentOutcome.PaymentFailed:
                    logger.LogInformation("Webhook Stripe {EventId} : paiement échoué/annulé", stripeEvent.Id);
                    break;
                case FulfillmentOutcome.OrderNotFound:
                    logger.LogWarning("Webhook Stripe {EventId} : commande introuvable ({Detail})", stripeEvent.Id, result.Detail);
                    break;
                case FulfillmentOutcome.AmountMismatch:
                    logger.LogError("Webhook Stripe {EventId} : montant incohérent ({Detail})", stripeEvent.Id, result.Detail);
                    break;
                case FulfillmentOutcome.CurrencyMismatch:
                    logger.LogError("Webhook Stripe {EventId} : devise incohérente ({Detail})", stripeEvent.Id, result.Detail);
                    break;
                case FulfillmentOutcome.StockUnavailable:
                    logger.LogError("Webhook Stripe {EventId} : conflit de stock ({Detail})", stripeEvent.Id, result.Detail);
                    break;
            }

            return Ok();
        }

        private async Task<IActionResult> HandleRefundEventAsync(Event stripeEvent)
        {
            if (stripeEvent.Data.Object is not Refund refund)
            {
                logger.LogWarning("Webhook Stripe {EventId} de type {EventType} sans objet Refund exploitable", stripeEvent.Id, stripeEvent.Type);
                return Ok();
            }

            // Le Metadata RefundRecordId (posé à la création, section 27) permet de
            // retrouver notre ligne Refund même si l'écriture de StripeRefundId après
            // l'appel synchrone a échoué (section 37) — StripeRefundId reste un repli.
            int? refundRecordId = null;
            if (refund.Metadata != null
                && refund.Metadata.TryGetValue("RefundRecordId", out var refundRecordIdRaw)
                && int.TryParse(refundRecordIdRaw, out var parsed))
            {
                refundRecordId = parsed;
            }

            var outcome = await refundOrchestrationService.ProcessRefundEventAsync(
                stripeEvent.Id, stripeEvent.Type, refund.Id, refundRecordId, refund.Status, refund.FailureReason);

            logger.LogInformation(
                "Webhook Stripe {EventId} (refund.updated, Refund Stripe {StripeRefundId}) : {Outcome}",
                stripeEvent.Id, refund.Id, outcome);

            return Ok();
        }
    }
}
