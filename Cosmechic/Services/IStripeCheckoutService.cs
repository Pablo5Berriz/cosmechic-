using Stripe.Checkout;

namespace Cosmechic.Services
{
    // COSMECHIC-ECOM-CORE-001 (section 27) : remplace les appels directs à
    // `new SessionService()` dispersés dans les controllers. Seule la création de
    // session est abstraite ici ; la vérification de paiement ne passe plus par un
    // polling de session (voir StripeWebhookController), donc l'ancien
    // IPaymentSessionService.Get() n'a plus de rôle dans ce lot et a été retiré.
    public interface IStripeCheckoutService
    {
        Session CreateSession(SessionCreateOptions options);
    }
}
