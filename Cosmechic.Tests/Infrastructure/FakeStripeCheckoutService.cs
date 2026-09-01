using Cosmechic.Services;
using Stripe.Checkout;

namespace Cosmechic.Tests.Infrastructure
{
    // Double de test pour IStripeCheckoutService : ne fait jamais d'appel réseau réel à
    // Stripe (COSMECHIC-ECOM-CORE-001, section 37 — aucun paiement réel, aucune clé
    // live). Enregistre la dernière requête de création de session pour permettre aux
    // tests de vérifier ce qui aurait été envoyé à Stripe (metadata, line items, etc.)
    // sans jamais y toucher réellement.
    public class FakeStripeCheckoutService : IStripeCheckoutService
    {
        public int CallCount { get; private set; }
        public SessionCreateOptions? LastOptions { get; private set; }
        public string SessionIdToReturn { get; set; } = "cs_test_fake";
        public string? PaymentIntentIdToReturn { get; set; } = "pi_test_fake";
        public string UrlToReturn { get; set; } = "https://stripe.test/checkout/cs_test_fake";
        public Exception? ExceptionToThrow { get; set; }

        public Session CreateSession(SessionCreateOptions options)
        {
            CallCount++;
            LastOptions = options;

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            return new Session
            {
                Id = SessionIdToReturn,
                Url = UrlToReturn,
                PaymentIntentId = PaymentIntentIdToReturn,
            };
        }
    }
}
