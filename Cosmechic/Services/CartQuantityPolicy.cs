namespace Cosmechic.Services
{
    // COSMECHIC-ECOM-CORE-001 (section 6) : règle serveur unique pour la quantité
    // demandée par ligne de panier. Le navigateur n'est jamais la source de vérité pour
    // la quantité ; cette règle est appliquée à la fois à l'ajout au panier
    // (ProduitsController.ItemDetails) et au checkout (CheckoutService), pour qu'un
    // POST direct (contournant le formulaire) ne puisse pas non plus la contourner.
    public static class CartQuantityPolicy
    {
        // Plafond arbitraire mais raisonnable : empêche une commande absurde (usage
        // abusif/automatisé) sans imposer de contrainte métier réelle sur le catalogue
        // actuel de Cosmechic.
        public const int MaxQuantityPerLine = 50;

        public static bool IsValidRequestedQuantity(int quantity)
            => quantity > 0 && quantity <= MaxQuantityPerLine;
    }
}
