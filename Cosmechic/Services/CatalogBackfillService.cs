using Cosmechic.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cosmechic.Services
{
    // COSMECHIC-CATALOG-001 (section 49/50) : rétro-remplissage déterministe et idempotent
    // des Slug/Sku pour les lignes historiques. Volontairement en C# (pas en SQL brut dans
    // la migration elle-même — voir SlugGenerator) pour une translittération fiable des
    // accents. Appelé explicitement une fois au démarrage (Program.cs) ; la requête
    // WHERE Slug IS NULL / Sku IS NULL est indexée (index filtré) et ne coûte rien une fois
    // le rattrapage terminé.
    public static class CatalogBackfillService
    {
        public static async Task RunAsync(CosmechicsContext context, ILogger logger)
        {
            var categoriesToFix = await context.Categories.Where(c => c.Slug == null).ToListAsync();
            if (categoriesToFix.Count > 0)
            {
                var existingCategorySlugs = new HashSet<string>(
                    await context.Categories.Where(c => c.Slug != null).Select(c => c.Slug!).ToListAsync(),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var category in categoriesToFix)
                {
                    category.Slug = MakeUniqueSlug(SlugGenerator.Slugify(category.Nom), existingCategorySlugs);
                }

                await context.SaveChangesAsync();
                logger.LogInformation("Rétro-remplissage catalogue : {Count} slug(s) de catégorie généré(s).", categoriesToFix.Count);
            }

            var produitsToFix = await context.Produits
                .Where(p => p.Slug == null || p.Sku == null)
                .ToListAsync();

            if (produitsToFix.Count > 0)
            {
                var existingProduitSlugs = new HashSet<string>(
                    await context.Produits.Where(p => p.Slug != null).Select(p => p.Slug!).ToListAsync(),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var produit in produitsToFix)
                {
                    produit.Slug ??= MakeUniqueSlug(SlugGenerator.Slugify(produit.Nom), existingProduitSlugs);

                    // SKU interne temporaire, clairement identifiable comme généré (jamais
                    // un SKU métier arbitraire) : à corriger par l'admin via Edit si une
                    // référence commerciale réelle existe.
                    produit.Sku ??= $"COS-{produit.ProduitId:D5}";
                }

                await context.SaveChangesAsync();
                logger.LogInformation("Rétro-remplissage catalogue : {Count} produit(s) complété(s) (slug/SKU).", produitsToFix.Count);
            }
        }

        private static string MakeUniqueSlug(string baseSlug, HashSet<string> existing)
        {
            if (string.IsNullOrEmpty(baseSlug))
            {
                baseSlug = "item";
            }

            var candidate = baseSlug;
            var suffix = 2;
            while (existing.Contains(candidate))
            {
                candidate = $"{baseSlug}-{suffix}";
                suffix++;
            }

            existing.Add(candidate);
            return candidate;
        }
    }
}
