namespace Cosmechic.Services
{
    public sealed record TaxLineResult(string Jurisdiction, decimal Rate, decimal Amount);

    public sealed record TaxCalculationResult(decimal TotalTaxAmount, IReadOnlyList<TaxLineResult> Lines);

    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 19/20) : mécanique fiscale testable,
    // sans jamais coder en dur une situation réglementaire pour une juridiction non déjà
    // établie dans l'application avant ce lot.
    public interface ITaxCalculator
    {
        Task<TaxCalculationResult> CalculateAsync(string countryCode, string? regionCode, decimal taxableSubtotal);
    }
}
