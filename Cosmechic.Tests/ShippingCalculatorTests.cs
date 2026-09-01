using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 47) : matrice directe d'IShippingCalculator,
    // indépendante de l'orchestration de CheckoutService.
    public class ShippingCalculatorTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly ShippingCalculator _sut;

        public ShippingCalculatorTests()
        {
            _sut = new ShippingCalculator(_context);
        }

        [Fact]
        public async Task NonExistentMethodId_ReturnsInvalid()
        {
            var result = await _sut.CalculateAsync(shippingMethodId: 9999, taxableSubtotal: 100m);

            Assert.IsType<ShippingMethodInvalid>(result);
        }

        [Fact]
        public async Task InactiveMethod_ReturnsInvalid()
        {
            var method = new ShippingMethod { Name = "Désactivée", Price = 10m, IsActive = false, SortOrder = 1 };
            _context.ShippingMethods.Add(method);
            _context.SaveChanges();

            var result = await _sut.CalculateAsync(method.ShippingMethodId, taxableSubtotal: 100m);

            Assert.IsType<ShippingMethodInvalid>(result);
        }

        [Fact]
        public async Task ActiveMethod_NoThreshold_ReturnsFullPrice()
        {
            var method = new ShippingMethod { Name = "Standard", Price = 15m, IsActive = true, SortOrder = 1 };
            _context.ShippingMethods.Add(method);
            _context.SaveChanges();

            var result = await _sut.CalculateAsync(method.ShippingMethodId, taxableSubtotal: 5m);

            var calculated = Assert.IsType<ShippingCalculated>(result);
            Assert.Equal(15m, calculated.Amount);
            Assert.Equal("Standard", calculated.ShippingMethodName);
            Assert.Equal(method.ShippingMethodId, calculated.ShippingMethodId);
        }

        [Fact]
        public async Task SubtotalBelowThreshold_ReturnsFullPrice()
        {
            var method = new ShippingMethod { Name = "Standard", Price = 15m, FreeShippingThreshold = 50m, IsActive = true, SortOrder = 1 };
            _context.ShippingMethods.Add(method);
            _context.SaveChanges();

            var result = await _sut.CalculateAsync(method.ShippingMethodId, taxableSubtotal: 49.99m);

            var calculated = Assert.IsType<ShippingCalculated>(result);
            Assert.Equal(15m, calculated.Amount);
        }

        [Fact]
        public async Task SubtotalExactlyAtThreshold_ReturnsZero()
        {
            var method = new ShippingMethod { Name = "Standard", Price = 15m, FreeShippingThreshold = 50m, IsActive = true, SortOrder = 1 };
            _context.ShippingMethods.Add(method);
            _context.SaveChanges();

            var result = await _sut.CalculateAsync(method.ShippingMethodId, taxableSubtotal: 50.00m);

            var calculated = Assert.IsType<ShippingCalculated>(result);
            Assert.Equal(0m, calculated.Amount);
        }

        [Fact]
        public async Task SubtotalAboveThreshold_ReturnsZero()
        {
            var method = new ShippingMethod { Name = "Standard", Price = 15m, FreeShippingThreshold = 50m, IsActive = true, SortOrder = 1 };
            _context.ShippingMethods.Add(method);
            _context.SaveChanges();

            var result = await _sut.CalculateAsync(method.ShippingMethodId, taxableSubtotal: 150m);

            var calculated = Assert.IsType<ShippingCalculated>(result);
            Assert.Equal(0m, calculated.Amount);
        }

        public void Dispose() => _context.Dispose();
    }
}
