using System.Linq;

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
    // Business{StreetAddress,City,Province,PostalCode,Country}, GstRegistrationStatus/
    // GstNumber, QstRegistrationStatus/QstNumber sont exactement INVOICE_LEGAL_TAX_INFO (voir
    // docs/audits/COSMECHIC-LEGAL-READINESS-001.md et
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
    // COSMECHIC-LEGAL-FINALIZATION-001 (section 4) : BusinessAddress (chaîne unique) éclatée
    // en champs structurés — cohérent avec la convention déjà utilisée partout ailleurs dans
    // ce dépôt pour une adresse (CustomerAddress, OrderHeader : Street/City/State/PostalCode/
    // CountryCode). BUSINESS_EMAIL/BUSINESS_PHONE ne sont PAS dupliqués ici : ce sont déjà
    // exactement SupportEmail/SupportPhone (ne jamais disperser la même donnée dans deux
    // champs — voir la directive section 5).
    public class BusinessInformationOptions
    {
        public string? LegalBusinessName { get; set; }
        public string? TradeName { get; set; }
        public string? BusinessStreetAddress { get; set; }
        public string? BusinessCity { get; set; }
        public string? BusinessProvince { get; set; }
        public string? BusinessPostalCode { get; set; }
        public string? BusinessCountry { get; set; }
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

    // COSMECHIC-LEGAL-FINALIZATION-001 (section 6) : LEGAL_CONFIGURATION_COMPLETE rendue
    // calculable/testable — sans jamais fabriquer une valeur pour la rendre "Complete". Une
    // future vue qui voudrait un jour afficher une facture fiscale doit interroger cet état
    // plutôt que de dupliquer sa propre logique de vérification (évite la dispersion —
    // section 5). NotApplicable existe pour un statut d'inscription tranché à
    // NotRegistered : aucun numéro n'est alors requis pour cette taxe, ce n'est ni
    // "manquant" ni "prêt à afficher un numéro" — un troisième état distinct, jamais
    // confondu avec Incomplete ni Complete.
    public enum LegalConfigurationState
    {
        Complete,
        Incomplete,
        NotApplicable,
    }

    // Pure, sans état, directement testable (même patron que WcagContrast dans
    // BrandContrastTests.cs) — jamais d'accès DB/réseau, uniquement les valeurs déjà
    // chargées de BusinessInformationOptions.
    public static class LegalConfigurationEvaluator
    {
        // Identité légale de base (nom + adresse complète) : jamais "NotApplicable" — soit
        // elle est fournie, soit elle manque.
        public static LegalConfigurationState EvaluateSellerIdentity(BusinessInformationOptions options)
        {
            var hasName = !string.IsNullOrWhiteSpace(options.LegalBusinessName);
            var hasAddress = !string.IsNullOrWhiteSpace(options.BusinessStreetAddress)
                && !string.IsNullOrWhiteSpace(options.BusinessCity)
                && !string.IsNullOrWhiteSpace(options.BusinessProvince)
                && !string.IsNullOrWhiteSpace(options.BusinessPostalCode)
                && !string.IsNullOrWhiteSpace(options.BusinessCountry);

            return hasName && hasAddress ? LegalConfigurationState.Complete : LegalConfigurationState.Incomplete;
        }

        public static LegalConfigurationState EvaluateGstRegistration(BusinessInformationOptions options) =>
            EvaluateTaxRegistration(options.GstRegistrationStatus, options.GstNumber);

        public static LegalConfigurationState EvaluateQstRegistration(BusinessInformationOptions options) =>
            EvaluateTaxRegistration(options.QstRegistrationStatus, options.QstNumber);

        private static LegalConfigurationState EvaluateTaxRegistration(TaxRegistrationStatus status, string? number) =>
            status switch
            {
                TaxRegistrationStatus.Unknown => LegalConfigurationState.Incomplete,
                TaxRegistrationStatus.NotRegistered => LegalConfigurationState.NotApplicable,
                TaxRegistrationStatus.Registered =>
                    string.IsNullOrWhiteSpace(number) ? LegalConfigurationState.Incomplete : LegalConfigurationState.Complete,
                _ => LegalConfigurationState.Incomplete,
            };

        // Vue d'ensemble : Complete seulement si l'identité vendeur ET les deux statuts de
        // taxe sont chacun Complete ou NotApplicable — jamais Complete tant qu'un seul
        // élément reste Incomplete (Unknown, ou Registered sans numéro). EvaluateSellerIdentity
        // n'est jamais NotApplicable (une identité vendeur est toujours pertinente) : la vue
        // d'ensemble ne peut donc jamais être NotApplicable elle-même, seulement Complete ou
        // Incomplete — pas de branche "tout NotApplicable" invraisemblable ici.
        public static LegalConfigurationState EvaluateOverall(BusinessInformationOptions options)
        {
            var states = new[]
            {
                EvaluateSellerIdentity(options),
                EvaluateGstRegistration(options),
                EvaluateQstRegistration(options),
            };

            return states.Any(s => s == LegalConfigurationState.Incomplete)
                ? LegalConfigurationState.Incomplete
                : LegalConfigurationState.Complete;
        }
    }
}
