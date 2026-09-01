using Stripe.Checkout;

namespace Cosmechic.Services
{
    public enum FulfillmentOutcome
    {
        Fulfilled,
        AlreadyProcessed,
        PaymentFailed,
        OrderNotFound,
        AmountMismatch,
        CurrencyMismatch,
        StockUnavailable,
    }

    public record FulfillmentResult(FulfillmentOutcome Outcome, string? Detail = null);

    // COSMECHIC-ECOM-CORE-001 (sections 12-25) : seul point d'entrée pour transformer un
    // événement Stripe checkout.session.* signé et vérifié en effet métier (paiement,
    // stock, fulfillment). N'est jamais appelé directement depuis le navigateur — voir
    // StripeWebhookController.
    public interface IStripeFulfillmentService
    {
        Task<FulfillmentResult> ProcessCheckoutSessionEventAsync(string stripeEventId, string eventType, Session session);
    }
}
