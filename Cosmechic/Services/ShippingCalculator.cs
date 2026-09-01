using Cosmechic.Models;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Services
{
    public class ShippingCalculator(CosmechicsContext context) : IShippingCalculator
    {
        public async Task<ShippingCalculationResult> CalculateAsync(int shippingMethodId, decimal taxableSubtotal)
        {
            var method = await context.ShippingMethods.FirstOrDefaultAsync(m => m.ShippingMethodId == shippingMethodId);

            // COSMECHIC-COMMERCE-OPERATIONS-001A (section 17/18) : le serveur charge le
            // prix depuis la base — jamais depuis le client. Une méthode inexistante ou
            // désactivée ne peut jamais être forcée par un POST direct.
            if (method == null || !method.IsActive)
            {
                return new ShippingMethodInvalid("Méthode de livraison invalide ou indisponible.");
            }

            var amount = method.Price;
            if (method.FreeShippingThreshold.HasValue && taxableSubtotal >= method.FreeShippingThreshold.Value)
            {
                amount = 0m;
            }

            return new ShippingCalculated(method.ShippingMethodId, method.Name, amount);
        }
    }
}
