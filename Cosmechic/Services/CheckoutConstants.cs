namespace Cosmechic.Services
{
    // Constante partagée entre CheckoutService (création de session) et
    // StripeFulfillmentService (validation du webhook) pour éviter une chaîne magique
    // dupliquée (COSMECHIC-ECOM-CORE-001, section 17).
    public static class CheckoutConstants
    {
        public const string Currency = "cad";
    }
}
