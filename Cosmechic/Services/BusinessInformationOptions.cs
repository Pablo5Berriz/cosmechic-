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
    // BusinessAddress, GstRegistrationStatus/GstNumber, QstRegistrationStatus/QstNumber sont
    // exactement INVOICE_LEGAL_TAX_INFO (voir docs/audits/COSMECHIC-LEGAL-READINESS-001.md et
    // docs/audits/COSMECHIC-LEGAL-DECISION-RESEARCH-001.md). Aucune valeur fictive n'a jamais
    // été saisie ici ni ailleurs — tant qu'elles restent vides/Unknown, le reçu
    // (Cart/Receipt.cshtml) continue d'afficher explicitement qu'il ne s'agit pas d'une
    // facture fiscale officielle plutôt que de prétendre à une conformité non prouvée.
    //
    // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 13) : scaffold de configuration
    // uniquement — remplace TaxRegistrationNumbers (chaîne générique, jamais consommée nulle
    // part) par les identifiants structurés exacts que le PM devra fournir avant toute
    // implémentation de facture fiscale conforme. GstRegistrationStatus/QstRegistrationStatus
    // par défaut à Unknown (jamais NotRegistered par défaut, qui serait lui-même une
    // affirmation factuelle non prouvée) — AUCUNE vue ne consomme ces champs tant qu'ils ne
    // sont pas explicitement renseignés ; le reçu existant reste inchangé.
    public class BusinessInformationOptions
    {
        public string? LegalBusinessName { get; set; }
        public string? TradeName { get; set; }
        public string? BusinessAddress { get; set; }
        public TaxRegistrationStatus GstRegistrationStatus { get; set; } = TaxRegistrationStatus.Unknown;
        public string? GstNumber { get; set; }
        public TaxRegistrationStatus QstRegistrationStatus { get; set; } = TaxRegistrationStatus.Unknown;
        public string? QstNumber { get; set; }
        public string SupportEmail { get; set; } = string.Empty;
        public string? SupportPhone { get; set; }
    }

    // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 13) : Unknown est le seul défaut
    // honnête — "non configuré" n'est ni "inscrit" ni "non inscrit", les deux étant des
    // affirmations factuelles que ce dépôt n'a jamais été autorisé à faire à la place du PM.
    public enum TaxRegistrationStatus
    {
        Unknown,
        NotRegistered,
        Registered,
    }
}
