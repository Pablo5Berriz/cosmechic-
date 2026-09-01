using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-ECOM-CORE-001 (section 31) : preuves ciblées que CheckoutService ne fait
    // jamais confiance au client pour une valeur financière, ne réserve/décrémente jamais
    // le stock à cette étape, et valide correctement la quantité demandée.
    public class CheckoutServiceTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly FakeStripeCheckoutService _stripeCheckoutService = new();
        private readonly OrderCheckoutService _sut;

        public CheckoutServiceTests()
        {
            _sut = new OrderCheckoutService(_context, _stripeCheckoutService, NullLogger<OrderCheckoutService>.Instance);
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

        private static ShippingAddress ValidShipping() => new("Alice", "5145551234", "1 rue Test", "Montreal", "QC", "H0H0H0");

        [Fact]
        public async Task EmptyCart_ReturnsFailed_NoOrderCreated()
        {
            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(), "https://cosmechic.test/");

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

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(), "https://cosmechic.test/");

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

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(), "https://cosmechic.test/");

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

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(), "https://cosmechic.test/");

            Assert.IsType<CheckoutFailed>(result);
        }

        [Fact]
        public async Task ValidCart_ComputesTotalServerSide_SnapshotsProductData_DoesNotTouchStock_CallsStripeOnce()
        {
            var produitId = SeedProduct(prix: 19.99m, stock: 10, nom: "Creme");
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 3 });
            _context.SaveChanges();

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(), "https://cosmechic.test/");

            var created = Assert.IsType<CheckoutSessionCreated>(result);
            Assert.Equal(1, _stripeCheckoutService.CallCount);

            var order = _context.OrderHeaders
                .Include(o => o.OrderDetails)
                .Single(o => o.Id == created.OrderHeaderId);

            // Total recalculé côté serveur (19.99 * 3), jamais issu d'un champ client :
            // ShippingAddress ne transporte aucune valeur financière (section 8).
            Assert.Equal(59.97m, order.OrderTotal);
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

            // Le stock n'est jamais touché à cette étape (section 5) : seul le
            // fulfillment webhook le décrémente.
            var stock = _context.Produits.AsNoTracking().Single(p => p.ProduitId == produitId).Stock;
            Assert.Equal(10, stock);
        }

        [Fact]
        public async Task StripeSessionCreationFails_ReturnsFailed()
        {
            var produitId = SeedProduct(prix: 25.00m, stock: 10);
            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = "user-a", ProduitId = produitId, Count = 1 });
            _context.SaveChanges();
            _stripeCheckoutService.ExceptionToThrow = new InvalidOperationException("simulated Stripe outage");

            var result = await _sut.CreateCheckoutSessionAsync("user-a", ValidShipping(), "https://cosmechic.test/");

            Assert.IsType<CheckoutFailed>(result);
        }

        public void Dispose() => _context.Dispose();
    }
}
