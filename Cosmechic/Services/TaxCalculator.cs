using Cosmechic.Models;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Services
{
    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 19/20/21) : somme toutes les taxes
    // actives pour la juridiction donnée (ex. Québec = TPS + TVQ, deux lignes distinctes),
    // arrondies séparément avant sommation — TAX_ROUNDING_POLICY : chaque ligne de taxe est
    // arrondie à 2 décimales (MidpointRounding.AwayFromZero) avant d'être additionnée aux
    // autres lignes. La taxe est calculée sur le sous-total uniquement (jamais sur les frais
    // de livraison, conformément à la convention déjà en place dans Cart/Summary.cshtml).
    // Aucune juridiction non déjà établie dans l'application n'est codée en dur ici : si
    // aucun TaxRate actif ne correspond, la taxe retournée est 0 (comportement explicite,
    // jamais une exception qui bloquerait le checkout pour une région non documentée).
    public class TaxCalculator(CosmechicsContext context) : ITaxCalculator
    {
        public async Task<TaxCalculationResult> CalculateAsync(string countryCode, string? regionCode, decimal taxableSubtotal)
        {
            var now = DateTime.UtcNow;

            var applicableRates = await context.TaxRates
                .Where(r => r.IsActive
                    && r.CountryCode == countryCode
                    && (r.RegionCode == null || r.RegionCode == regionCode)
                    && r.EffectiveFrom <= now
                    && (r.EffectiveTo == null || r.EffectiveTo > now))
                .OrderBy(r => r.Jurisdiction)
                .ToListAsync();

            var lines = new List<TaxLineResult>();
            decimal total = 0m;

            foreach (var rate in applicableRates)
            {
                var lineAmount = Math.Round(taxableSubtotal * rate.Rate, 2, MidpointRounding.AwayFromZero);
                lines.Add(new TaxLineResult(rate.Jurisdiction, rate.Rate, lineAmount));
                total += lineAmount;
            }

            return new TaxCalculationResult(total, lines);
        }
    }
}
