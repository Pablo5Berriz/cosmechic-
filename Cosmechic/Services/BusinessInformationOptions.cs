namespace Cosmechic.Services
{
    // COSMECHIC-CONTENT-LEGAL-001 (section 6/20) : seule source de vérité pour les
    // informations d'entreprise affichées sur les pages institutionnelles/légales
    // (À propos, Contact, Confidentialité, Conditions, reçu). Toutes les valeurs
    // viennent de la configuration — jamais fabriquées en dur dans une vue — et sont
    // nullable/vides par défaut : une page qui en a besoin doit explicitement gérer le
    // cas "non configuré" plutôt que d'afficher une valeur inventée. SupportEmail est la
    // seule pré-remplie car elle correspond à une valeur déjà réellement configurée
    // ailleurs (Smtp:FromAddress).
    // COSMECHIC-LEGAL-READINESS-001 : TODO_REQUIRES_LEGAL_REVIEW — LegalBusinessName,
    // BusinessAddress et TaxRegistrationNumbers sont exactement INVOICE_LEGAL_TAX_INFO
    // (voir docs/audits/COSMECHIC-LEGAL-READINESS-001.md). Aucune valeur fictive n'a
    // jamais été saisie ici ni ailleurs — tant qu'elles restent vides, le reçu
    // (Cart/Receipt.cshtml) continue d'afficher explicitement qu'il ne s'agit pas d'une
    // facture fiscale officielle plutôt que de prétendre à une conformité non prouvée.
    public class BusinessInformationOptions
    {
        public string? LegalBusinessName { get; set; }
        public string? BusinessAddress { get; set; }
        public string? TaxRegistrationNumbers { get; set; }
        public string SupportEmail { get; set; } = string.Empty;
        public string? SupportPhone { get; set; }
    }
}
