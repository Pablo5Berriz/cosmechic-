using Stripe.Checkout;

namespace Cosmechic.Services
{
    // Implémentation par défaut : délègue directement au SDK Stripe.net, comportement
    // de production inchangé.
    public class StripePaymentSessionService : IPaymentSessionService
    {
        public Session Get(string? sessionId)
        {
            var service = new SessionService();
            return service.Get(sessionId);
        }
    }
}
