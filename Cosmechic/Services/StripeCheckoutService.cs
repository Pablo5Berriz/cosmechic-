using Stripe.Checkout;

namespace Cosmechic.Services
{
    // Implémentation par défaut : délègue directement au SDK Stripe.net, comportement
    // de production inchangé par rapport à l'ancien `new SessionService().Create(...)`.
    public class StripeCheckoutService : IStripeCheckoutService
    {
        public Session CreateSession(SessionCreateOptions options)
        {
            var service = new SessionService();
            return service.Create(options);
        }
    }
}
