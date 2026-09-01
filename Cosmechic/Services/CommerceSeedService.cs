using Cosmechic.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cosmechic.Services
{
    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 5) : amorçage ponctuel et idempotent des
    // valeurs déjà décidées par l'application AVANT ce lot (visibles jusqu'ici uniquement en
    // dur, côté vue, dans Cart/Summary.cshtml : TPS_RATE 5 %, TVQ_RATE 9.975 %,
    // SHIPPING_COST 15,00 $) — migrées ici vers le modèle configurable ShippingMethod/TaxRate
    // au lieu d'être ré-inventées. Aucune nouvelle règle métier n'est ajoutée : un seul
    // seuil de livraison gratuite ou une nouvelle juridiction resteraient
    // TODO_REQUIRES_BUSINESS_CONFIGURATION, configurables ensuite par un admin (section 22).
    // Suit le même patron que CatalogBackfillService : idempotent, non bloquant au démarrage.
    public static class CommerceSeedService
    {
        public static async Task RunAsync(CosmechicsContext context, ILogger logger)
        {
            if (!await context.ShippingMethods.AnyAsync())
            {
                context.ShippingMethods.Add(new ShippingMethod
                {
                    Name = "Livraison standard",
                    Description = "Livraison standard partout au Canada.",
                    Price = 15.00m,
                    FreeShippingThreshold = null,
                    IsActive = true,
                    SortOrder = 1,
                });

                await context.SaveChangesAsync();
                logger.LogInformation("Amorçage commerce : méthode de livraison par défaut créée (15,00 CAD).");
            }

            if (!await context.TaxRates.AnyAsync())
            {
                var effectiveFrom = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

                context.TaxRates.AddRange(
                    new TaxRate
                    {
                        Jurisdiction = "TPS (fédérale)",
                        CountryCode = RegionCodeResolver.CountryCodeCanada,
                        RegionCode = null,
                        Rate = 0.05m,
                        EffectiveFrom = effectiveFrom,
                        EffectiveTo = null,
                        IsActive = true,
                    },
                    new TaxRate
                    {
                        Jurisdiction = "TVQ (Québec)",
                        CountryCode = RegionCodeResolver.CountryCodeCanada,
                        RegionCode = "QC",
                        Rate = 0.09975m,
                        EffectiveFrom = effectiveFrom,
                        EffectiveTo = null,
                        IsActive = true,
                    });

                await context.SaveChangesAsync();
                logger.LogInformation("Amorçage commerce : taux de taxe TPS/TVQ créés.");
            }
        }
    }
}
