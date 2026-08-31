using Cosmechic.Services;
using Stripe.Checkout;

namespace Cosmechic.Tests.Infrastructure
{
    // Double de test pour IPaymentSessionService : ne fait jamais d'appel réseau réel à
    // Stripe. Enregistre si/avec quel sessionId Get() a été invoqué, pour prouver
    // l'absence d'appel Stripe sur une commande non autorisée (COSMECHIC-SECURITY-001,
    // section 17).
    public class FakePaymentSessionService : IPaymentSessionService
    {
        public int CallCount { get; private set; }
        public string? LastSessionIdRequested { get; private set; }
        public string ResultPaymentStatus { get; set; } = "paid";

        public Session Get(string? sessionId)
        {
            CallCount++;
            LastSessionIdRequested = sessionId;
            return new Session
            {
                Id = sessionId,
                PaymentStatus = ResultPaymentStatus,
                PaymentIntentId = "pi_test_fake",
            };
        }
    }
}
