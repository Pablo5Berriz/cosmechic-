using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 47) : matrice directe d'ITaxCalculator,
    // indépendante de l'orchestration de CheckoutService. Aucune juridiction n'est codée en
    // dur dans le calculateur lui-même — tout vient de TaxRate, y compris l'absence de taux
    // (0 $, jamais une exception).
    public class TaxCalculatorTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly TaxCalculator _sut;

        public TaxCalculatorTests()
        {
            _sut = new TaxCalculator(_context);
        }

        private void AddRate(string jurisdiction, string country, string? region, decimal rate, bool isActive = true, DateTime? from = null, DateTime? to = null)
        {
            _context.TaxRates.Add(new TaxRate
            {
                Jurisdiction = jurisdiction,
                CountryCode = country,
                RegionCode = region,
                Rate = rate,
                EffectiveFrom = from ?? DateTime.UtcNow.AddYears(-1),
                EffectiveTo = to,
                IsActive = isActive,
            });
            _context.SaveChanges();
        }

        [Fact]
        public async Task NoRatesConfigured_ReturnsZero_NoException()
        {
            var result = await _sut.CalculateAsync("CA", "QC", 100m);

            Assert.Equal(0m, result.TotalTaxAmount);
            Assert.Empty(result.Lines);
        }

        [Fact]
        public async Task RegionSpecificRateOnly_AppliesOnlyToMatchingRegion()
        {
            AddRate("TVQ (Québec)", "CA", "QC", 0.09975m);

            var quebecResult = await _sut.CalculateAsync("CA", "QC", 100m);
            var ontarioResult = await _sut.CalculateAsync("CA", "ON", 100m);

            Assert.Equal(9.98m, quebecResult.TotalTaxAmount);
            Assert.Equal(0m, ontarioResult.TotalTaxAmount);
        }

        [Fact]
        public async Task CountryWideRate_NullRegionCode_AppliesToEveryRegion()
        {
            AddRate("TPS (fédérale)", "CA", null, 0.05m);

            var quebecResult = await _sut.CalculateAsync("CA", "QC", 100m);
            var ontarioResult = await _sut.CalculateAsync("CA", "ON", 100m);
            var noRegionResult = await _sut.CalculateAsync("CA", null, 100m);

            Assert.Equal(5.00m, quebecResult.TotalTaxAmount);
            Assert.Equal(5.00m, ontarioResult.TotalTaxAmount);
            Assert.Equal(5.00m, noRegionResult.TotalTaxAmount);
        }

        [Fact]
        public async Task QuebecOrder_SumsFederalAndProvincialRates_TwoDistinctLines()
        {
            AddRate("TPS (fédérale)", "CA", null, 0.05m);
            AddRate("TVQ (Québec)", "CA", "QC", 0.09975m);

            var result = await _sut.CalculateAsync("CA", "QC", 100m);

            Assert.Equal(2, result.Lines.Count);
            Assert.Equal(5.00m + 9.98m, result.TotalTaxAmount);
        }

        [Fact]
        public async Task InactiveRate_IsExcluded()
        {
            AddRate("Taux désactivé", "CA", "QC", 0.50m, isActive: false);

            var result = await _sut.CalculateAsync("CA", "QC", 100m);

            Assert.Equal(0m, result.TotalTaxAmount);
        }

        [Fact]
        public async Task RateNotYetEffective_IsExcluded()
        {
            AddRate("Taux futur", "CA", "QC", 0.50m, from: DateTime.UtcNow.AddDays(30));

            var result = await _sut.CalculateAsync("CA", "QC", 100m);

            Assert.Equal(0m, result.TotalTaxAmount);
        }

        [Fact]
        public async Task ExpiredRate_IsExcluded()
        {
            AddRate("Taux expiré", "CA", "QC", 0.50m, from: DateTime.UtcNow.AddYears(-2), to: DateTime.UtcNow.AddDays(-1));

            var result = await _sut.CalculateAsync("CA", "QC", 100m);

            Assert.Equal(0m, result.TotalTaxAmount);
        }

        [Fact]
        public async Task DifferentCountryCode_DoesNotMatch()
        {
            AddRate("Taux CA", "CA", "QC", 0.50m);

            var result = await _sut.CalculateAsync("US", "QC", 100m);

            Assert.Equal(0m, result.TotalTaxAmount);
        }

        [Fact]
        public async Task RoundingUsesAwayFromZero_OnMidpointValue()
        {
            // 0.05 * 0.5 = 0.025 exactement (décimal base 10) : AwayFromZero arrondit à
            // 0.03 ; un arrondi bancaire (ToEven) donnerait 0.02.
            AddRate("Taux test arrondi", "CA", "QC", 0.5m);

            var result = await _sut.CalculateAsync("CA", "QC", 0.05m);

            Assert.Equal(0.03m, result.TotalTaxAmount);
        }

        public void Dispose() => _context.Dispose();
    }
}
