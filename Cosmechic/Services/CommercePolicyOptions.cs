namespace Cosmechic.Services
{
    // COSMECHIC-CONTENT-LEGAL-001 (section 12/28) : centralise les trois décisions
    // commerciales/fiscales encore ouvertes depuis COSMECHIC-COMMERCE-OPERATIONS-001B
    // (RETURN_WINDOW_DAYS/REFUND_SHIPPING_POLICY/REFUND_TAX_POLICY). Nullable par
    // défaut — aucune valeur n'est fabriquée ici ; une page qui les affiche doit gérer
    // explicitement le cas "non configuré" (TODO_REQUIRES_BUSINESS_CONFIGURATION)
    // plutôt que d'inventer un délai ou une politique. Centralisé plutôt que dupliqué en
    // texte libre dans la FAQ/la page Retours/les Conditions (section 6).
    public class CommercePolicyOptions
    {
        public int? ReturnWindowDays { get; set; }
        public string? RefundShippingPolicy { get; set; }
        public string? RefundTaxPolicy { get; set; }
    }
}
