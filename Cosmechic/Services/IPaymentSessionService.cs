using Stripe.Checkout;

namespace Cosmechic.Services
{
    // Abstraction minimale, introduite uniquement pour rendre CartController.OrderConfirmation
    // testable sans appel réseau réel à Stripe (COSMECHIC-SECURITY-001, preuve d'absence
    // d'appel Stripe pour une commande non autorisée). N'abstrait pas le reste de
    // l'intégration Stripe (création de session, etc.) : hors périmètre de ce lot.
    public interface IPaymentSessionService
    {
        Session Get(string? sessionId);
    }
}
