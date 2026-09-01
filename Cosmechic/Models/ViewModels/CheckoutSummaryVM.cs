namespace Cosmechic.Models.ViewModels
{
    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 44) : modèle d'affichage de Summary (GET).
    // Porte les données nécessaires à l'aperçu client (sous-total, méthodes de livraison
    // actives, taux de taxe actifs pour l'aperçu JS) — jamais utilisé pour la commande réelle,
    // qui est toujours recalculée côté serveur dans OrderCheckoutService à partir de la base.
    public class CheckoutSummaryVM
    {
        public IEnumerable<ShoppingCart> ShoppingCartList { get; set; } = new List<ShoppingCart>();
        public decimal Subtotal { get; set; }
        public List<ShippingMethod> ShippingMethods { get; set; } = new();
        public List<TaxRate> ActiveTaxRates { get; set; } = new();
        public CheckoutFormInput Input { get; set; } = new();

        // COSMECHIC-ACCOUNT-001 (section 15) : adresses enregistrées du client, pour lui
        // permettre d'en sélectionner une plutôt que de ressaisir une adresse ponctuelle.
        public IReadOnlyList<CustomerAddress> SavedAddresses { get; set; } = Array.Empty<CustomerAddress>();
    }
}
