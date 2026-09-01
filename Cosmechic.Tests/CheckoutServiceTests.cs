using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-ECOM-CORE-001 (section 31) / COSMECHIC-COMMERCE-OPERATIONS-001A (section 47) :
    // preuves ciblées que CheckoutService ne fait jamais confiance au client pour une valeur
    // financière, ne réserve/décrémente jamais le stock à cette étape, valide correctement la
    // quantité demandée, et calcule Subtotal/ShippingAmount/TaxAmount/DiscountAmount/OrderTotal
    // exclusivement à partir de l'état serveur (panier, ShippingMethod, TaxRate actifs).
    public class CheckoutServiceTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly FakeStripeCheckoutService _stripeCheckoutService = new();
        private readonly OrderCheckoutService _sut;

        public CheckoutServiceTests()
        {
            _sut = new OrderCheckoutService(
                _context,
                _stripeCheckoutService,
                new ShippingCalculator(_context),
                new TaxCalculator(_context),
                NullLogger<OrderCheckoutService>.Instance);
        }

        private int SeedProduct(decimal prix, decimal stock, bool disponible = true, string nom = "Produit test")
        {
            var categorie = new Category { Nom = "Cat", Image = "c.jpg", Disponible = true };
            _context.Categories.Add(categorie);
            _context.SaveChanges();

            var produit = new Produit
            {
                Nom = nom,
                CategorieId = categorie.CategorieId,
                Prix = prix,
                Stock = stock,
                Disponible = disponible,
                Image = "p.jpg",
                RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
            };
            _context.Produits.Add(produit);
            _context.SaveChanges();
            return produit.ProduitId;
        }

        private int SeedShippingMethod(decimal price, decimal? freeShippingThreshold = null, bool isActive = true, string name = "Livraison standard")
        {
            var method = new ShippingMethod
            {
                Name = name,
                Price = price,
                FreeShippingThreshold = freeShippingThreshold,
                IsActive = isActive,
                SortOrder = 1,
            };
            _context.ShippingMethods.Add(method);
            _context.SaveChanges();
            return method.ShippingMethodId;
        }

        private void SeedTaxRate(string jurisdiction, string countryCode, string? regionCode, decimal rate)
        {
            _context.TaxRates.Add(new TaxRate
            {
                Jurisdiction = jurisdiction,
                CountryCode = countryCode,
                RegionCode = regionCode,
                Rate = rate,
                EffectiveFrom = DateTime.UtcNow.AddYears(-1),
                EffectiveTo = null,
                IsActive = true,
            });
            _context.SaveChanges();
        }

        private static ShippingAddress ValidShipping(int shippingMethodId, string state = "QC") =>
            new("Alice", "5145551234", "1 rue Test", "Montreal", state, "H0H0H0", shippingMethodId);

        [Fact]
        public async Task EmptyCart_ReturnsFailed_NoOrderCreated()
        {
            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(0), "https://cosmechic.test/");

            Assert.IsType<CheckoutFailed>(result);
            Assert.Empty(_context.OrderHeaders);
            Assert.Equal(0, _stripeCheckoutService.CallCount);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(51)]
        public async Task InvalidQuantity_ReturnsFailed_NoOrderCreated_NoStripeCall(int invalidCount)
        {
            var produitId = SeedProduct(prix: 25.00m, stock: 100);
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = invalidCount });
            _context.SaveChanges();

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(0), "https://cosmechic.test/");

            Assert.IsType<CheckoutFailed>(result);
            Assert.Empty(_context.OrderHeaders);
            Assert.Equal(0, _stripeCheckoutService.CallCount);
        }

        [Fact]
        public async Task InsufficientStock_ReturnsFailed_NoOrderCreated_StockUnchanged()
        {
            var produitId = SeedProduct(prix: 25.00m, stock: 2);
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 5 });
            _context.SaveChanges();

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(0), "https://cosmechic.test/");

            Assert.IsType<CheckoutFailed>(result);
            Assert.Empty(_context.OrderHeaders);
            var stock = _context.Produits.AsNoTracking().Single(p => p.ProduitId == produitId).Stock;
            Assert.Equal(2, stock);
        }

        [Fact]
        public async Task UnavailableProduct_ReturnsFailed()
        {
            var produitId = SeedProduct(prix: 25.00m, stock: 10, disponible: false);
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 1 });
            _context.SaveChanges();

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(0), "https://cosmechic.test/");

            Assert.IsType<CheckoutFailed>(result);
        }

        [Fact]
        public async Task ValidCart_ComputesSubtotalShippingTaxAndTotal_ServerSide_SnapshotsProductAndShippingData_DoesNotTouchStock()
        {
            var produitId = SeedProduct(prix: 19.99m, stock: 10, nom: "Creme");
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 3 });
            var shippingMethodId = SeedShippingMethod(price: 15.00m, name: "Livraison standard");
            SeedTaxRate("TPS (fédérale)", "CA", null, 0.05m);
            SeedTaxRate("TVQ (Québec)", "CA", "QC", 0.09975m);
            _context.SaveChanges();

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(shippingMethodId, "QC"), "https://cosmechic.test/");

            var created = Assert.IsType<CheckoutSessionCreated>(result);
            Assert.Equal(1, _stripeCheckoutService.CallCount);

            var order = _context.OrderHeaders
                .Include(o => o.OrderDetails)
                .Single(o => o.Id == created.OrderHeaderId);

            // Subtotal recalculé côté serveur (19.99 * 3), jamais issu d'un champ client :
            // ShippingAddress ne transporte que ShippingMethodId, aucune valeur financière
            // (COSMECHIC-COMMERCE-OPERATIONS-001A, section 41).
            Assert.Equal(59.97m, order.Subtotal);
            Assert.Equal(15.00m, order.ShippingAmount);
            Assert.Equal(shippingMethodId, order.ShippingMethodId);
            Assert.Equal("Livraison standard", order.ShippingMethodName);
            // 59.97 * (0.05 + 0.09975) = 59.97 * 0.14975 = 8.980509 -> chaque ligne
            // arrondie séparément (TAX_ROUNDING_POLICY) : 59.97*0.05=2.9985 -> 3.00 ;
            // 59.97*0.09975=5.982009 -> 5.98 ; somme = 8.98.
            Assert.Equal(8.98m, order.TaxAmount);
            Assert.Equal(0m, order.DiscountAmount);
            Assert.Equal(59.97m + 15.00m + 8.98m, order.OrderTotal);
            Assert.Equal(order.Subtotal + order.ShippingAmount + order.TaxAmount - order.DiscountAmount, order.OrderTotal);

            Assert.Equal("user-a", order.ApplicationUserId);
            Assert.Equal(Cosmechic.Utility.SD.StatusPending, order.OrderStatus);
            Assert.Equal(Cosmechic.Utility.SD.PaymentStatusPending, order.PaymentStatus);

            var detail = Assert.Single(order.OrderDetails);
            Assert.Equal(19.99m, detail.Price);
            Assert.Equal("Creme", detail.ProduitNom);
            Assert.Equal(3, detail.Count);

            Assert.Equal(_stripeCheckoutService.SessionIdToReturn, order.SessionId);
            Assert.True(_stripeCheckoutService.LastOptions!.Metadata.ContainsKey("OrderId"));
            Assert.Equal(created.OrderHeaderId.ToString(), _stripeCheckoutService.LastOptions.Metadata["OrderId"]);

            // Stripe doit facturer exactement OrderTotal, pas seulement le sous-total
            // (COSMECHIC-COMMERCE-OPERATIONS-001A, section 1/26/27).
            var expectedCents = (long)Math.Round(order.OrderTotal * 100, MidpointRounding.AwayFromZero);
            var actualCents = _stripeCheckoutService.LastOptions.LineItems.Sum(li => li.PriceData.UnitAmount!.Value * (li.Quantity ?? 1));
            Assert.Equal(expectedCents, actualCents);

            // Le stock n'est jamais touché à cette étape (section 5) : seul le
            // fulfillment webhook le décrémente.
            var stock = _context.Produits.AsNoTracking().Single(p => p.ProduitId == produitId).Stock;
            Assert.Equal(10, stock);
        }

        [Fact]
        public async Task InvalidShippingMethodId_ReturnsFailed_NoOrderCreated_NoStripeCall()
        {
            var produitId = SeedProduct(prix: 25.00m, stock: 10);
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 1 });
            _context.SaveChanges();

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(shippingMethodId: 9999), "https://cosmechic.test/");

            Assert.IsType<CheckoutFailed>(result);
            Assert.Empty(_context.OrderHeaders);
            Assert.Equal(0, _stripeCheckoutService.CallCount);
        }

        [Fact]
        public async Task InactiveShippingMethod_ReturnsFailed_NoOrderCreated()
        {
            var produitId = SeedProduct(prix: 25.00m, stock: 10);
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 1 });
            var shippingMethodId = SeedShippingMethod(price: 15.00m, isActive: false);
            _context.SaveChanges();

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(shippingMethodId), "https://cosmechic.test/");

            Assert.IsType<CheckoutFailed>(result);
            Assert.Empty(_context.OrderHeaders);
            Assert.Equal(0, _stripeCheckoutService.CallCount);
        }

        [Fact]
        public async Task FreeShippingThreshold_SubtotalAtThreshold_ShippingIsZero()
        {
            var produitId = SeedProduct(prix: 50.00m, stock: 10);
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 1 });
            var shippingMethodId = SeedShippingMethod(price: 15.00m, freeShippingThreshold: 50.00m);
            _context.SaveChanges();

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(shippingMethodId), "https://cosmechic.test/");

            var created = Assert.IsType<CheckoutSessionCreated>(result);
            var order = _context.OrderHeaders.Single(o => o.Id == created.OrderHeaderId);
            Assert.Equal(0m, order.ShippingAmount);
        }

        [Fact]
        public async Task FreeShippingThreshold_SubtotalBelowThreshold_ShippingIsFullPrice()
        {
            var produitId = SeedProduct(prix: 49.99m, stock: 10);
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 1 });
            var shippingMethodId = SeedShippingMethod(price: 15.00m, freeShippingThreshold: 50.00m);
            _context.SaveChanges();

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(shippingMethodId), "https://cosmechic.test/");

            var created = Assert.IsType<CheckoutSessionCreated>(result);
            var order = _context.OrderHeaders.Single(o => o.Id == created.OrderHeaderId);
            Assert.Equal(15.00m, order.ShippingAmount);
        }

        [Fact]
        public async Task NoMatchingTaxRate_ResultsInZeroTax_NeverAnException()
        {
            var produitId = SeedProduct(prix: 100.00m, stock: 10);
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 1 });
            var shippingMethodId = SeedShippingMethod(price: 10.00m);
            // Aucun TaxRate seedé : aucune juridiction non déjà établie n'est codée en dur
            // (COSMECHIC-COMMERCE-OPERATIONS-001A, section 20) — TODO_REQUIRES_BUSINESS_CONFIGURATION.
            _context.SaveChanges();

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(shippingMethodId, "ON"), "https://cosmechic.test/");

            var created = Assert.IsType<CheckoutSessionCreated>(result);
            var order = _context.OrderHeaders.Single(o => o.Id == created.OrderHeaderId);
            Assert.Equal(0m, order.TaxAmount);
            Assert.Equal(100.00m + 10.00m, order.OrderTotal);
        }

        [Fact]
        public async Task InactiveTaxRate_IsIgnored()
        {
            var produitId = SeedProduct(prix: 100.00m, stock: 10);
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 1 });
            var shippingMethodId = SeedShippingMethod(price: 0m);
            _context.TaxRates.Add(new TaxRate
            {
                Jurisdiction = "Taux désactivé",
                CountryCode = "CA",
                RegionCode = "QC",
                Rate = 0.50m,
                EffectiveFrom = DateTime.UtcNow.AddYears(-1),
                IsActive = false,
            });
            _context.SaveChanges();

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(shippingMethodId, "QC"), "https://cosmechic.test/");

            var created = Assert.IsType<CheckoutSessionCreated>(result);
            var order = _context.OrderHeaders.Single(o => o.Id == created.OrderHeaderId);
            Assert.Equal(0m, order.TaxAmount);
        }

        [Fact]
        public async Task TaxRounding_UsesAwayFromZero_OnEachLineBeforeSumming()
        {
            // 0.05 * 0.5 = 0.025 exactement (décimal base 10) : cas limite volontaire pour
            // isoler la politique d'arrondi (TAX_ROUNDING_POLICY), indépendant de tout taux
            // réel — AwayFromZero arrondit 0.025 à 0.03, un arrondi bancaire (ToEven)
            // donnerait 0.02.
            var produitId = SeedProduct(prix: 0.05m, stock: 10);
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 1 });
            var shippingMethodId = SeedShippingMethod(price: 0m);
            SeedTaxRate("Taux test arrondi", "CA", "QC", 0.5m);
            _context.SaveChanges();

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(shippingMethodId, "QC"), "https://cosmechic.test/");

            var created = Assert.IsType<CheckoutSessionCreated>(result);
            var order = _context.OrderHeaders.Single(o => o.Id == created.OrderHeaderId);
            Assert.Equal(0.03m, order.TaxAmount);
        }

        [Fact]
        public async Task StripeSessionCreationFails_ReturnsFailed()
        {
            var produitId = SeedProduct(prix: 25.00m, stock: 10);
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 1 });
            var shippingMethodId = SeedShippingMethod(price: 5.00m);
            _context.SaveChanges();
            _stripeCheckoutService.ExceptionToThrow = new InvalidOperationException("simulated Stripe outage");

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(shippingMethodId), "https://cosmechic.test/");

            Assert.IsType<CheckoutFailed>(result);
        }

        public void Dispose() => _context.Dispose();
    }
}
