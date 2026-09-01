using System.Globalization;
using System.Text;

namespace Cosmechic.Services
{
    // COSMECHIC-CATALOG-001 (section 17/49) : génération de slug déterministe, en C#
    // plutôt qu'en SQL — une normalisation Unicode (NFD + suppression des marques
    // diacritiques) est fiable et testée, contrairement à un cast/collation SQL Server qui
    // ne garantit pas la translittération (vérifié empiriquement : COLLATE Latin1_General_CI_AI
    // ne modifie pas la chaîne, seulement les comparaisons).
    public static class SlugGenerator
    {
        public static string Slugify(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var normalized = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(c) && c < 128)
                {
                    builder.Append(c);
                }
                else if (c is '-' or ' ' or '_')
                {
                    builder.Append('-');
                }
            }

            var slug = builder.ToString().Normalize(NormalizationForm.FormC);

            while (slug.Contains("--"))
            {
                slug = slug.Replace("--", "-");
            }

            return slug.Trim('-');
        }
    }
}
