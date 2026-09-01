namespace Cosmechic.Services
{
    // COSMECHIC-CONTENT-LEGAL-001 (section 6/20) : seule source de vérité pour les
    // informations d'entreprise affichées sur les pages institutionnelles/légales
    // (À propos, Contact, Confidentialité, Conditions, reçu). Toutes les valeurs
    // viennent de la configuration — jamais fabriquées en dur dans une vue — et sont
    // nullable/vides par défaut : une page qui en a besoin doit explicitement gérer le
    // cas "non configuré" (TODO_REQUIRES_BUSINESS_CONFIGURATION) plutôt que d'afficher
    // une valeur inventée. SupportEmail est la seule pré-remplie car elle correspond à
    // une valeur déjà réellement configurée ailleurs (Smtp:FromAddress).
    public class BusinessInformationOptions
    {
        public string? LegalBusinessName { get; set; }
        public string? BusinessAddress { get; set; }
        public string? TaxRegistrationNumbers { get; set; }
        public string SupportEmail { get; set; } = string.Empty;
        public string? SupportPhone { get; set; }
    }
}
