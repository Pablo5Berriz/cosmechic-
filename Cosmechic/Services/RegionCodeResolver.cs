using System.Globalization;
using System.Text;

namespace Cosmechic.Services
{
    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 20) : normalisation minimale et sûre
    // (Trim, comparaison insensible à la casse/aux accents — même technique NFD que
    // SlugGenerator, COSMECHIC-CATALOG-001) du champ libre "province" saisi par le client
    // vers un code de région utilisable par ITaxCalculator. Aucun appel externe, aucune
    // table de référence complète des provinces canadiennes : seule la juridiction déjà
    // établie dans l'application avant ce lot (Québec, pour la TVQ) est reconnue
    // explicitement. Toute autre valeur à deux lettres est traitée comme un code déjà
    // normalisé ; sinon, aucune région n'est retenue (TaxRate.RegionCode == null continue
    // de s'appliquer, ex. TPS fédérale si un jour configurée ainsi).
    public static class RegionCodeResolver
    {
        public const string CountryCodeCanada = "CA";

        private static readonly HashSet<string> QuebecVariants = new(StringComparer.Ordinal)
        {
            "QC", "QUEBEC",
        };

        public static string? ResolveCanadianRegionCode(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return null;
            }

            var withoutAccents = RemoveDiacritics(state.Trim()).ToUpperInvariant();

            if (QuebecVariants.Contains(withoutAccents))
            {
                return "QC";
            }

            return withoutAccents.Length == 2 ? withoutAccents : null;
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
