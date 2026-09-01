namespace Cosmechic.Services
{
    public abstract record ShippingCalculationResult;

    public sealed record ShippingCalculated(int ShippingMethodId, string ShippingMethodName, decimal Amount) : ShippingCalculationResult;

    public sealed record ShippingMethodInvalid(string Reason) : ShippingCalculationResult;

    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 16/17) : abstraction simple et
    // testable. Aucune intégration transporteur (FedEx/UPS/Postes Canada) — un modèle
    // configurable en base suffit pour ce lot ; le client envoie un ShippingMethodId,
    // jamais un montant.
    public interface IShippingCalculator
    {
        Task<ShippingCalculationResult> CalculateAsync(int shippingMethodId, decimal taxableSubtotal);
    }
}
