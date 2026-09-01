namespace Cosmechic.Utility
{
    public class StripeSettings
    {
        public string SecretKey { get; set; }
        public string PublishableKey { get; set; }
        // COSMECHIC-ECOM-CORE-001 : secret de signature du endpoint webhook Stripe.
        // Jamais en dur dans le code, toujours depuis la configuration (variable
        // d'environnement / user-secrets / gestionnaire de secrets en production).
        public string WebhookSecret { get; set; } = string.Empty;
    }
}
