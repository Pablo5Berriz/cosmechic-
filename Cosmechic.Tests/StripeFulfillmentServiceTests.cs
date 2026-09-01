using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe.Checkout;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-ECOM-CORE-001 (section 31) : preuves ciblées de la logique de
    // StripeFulfillmentService au niveau applicatif (InMemory). La garantie réelle contre
    // la concurrence (contrainte UNIQUE, RowVersion) est vérifiée séparément contre un
    // vrai SQL Server jetable — voir SqlServerFulfillmentConcurrencyTests.
    public class StripeFulfillmentServiceTests : IDisposable
    {
        private const string UserId = "user-a";
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly StripeFulfillmentService _sut;

        public StripeFulfillmentServiceTests()
        {
            _sut = new StripeFulfillmentService(_context, NullLogger<StripeFulfillmentService>.Instance);
        }

        private (int OrderId, int ProduitId) SeedPendingOrder(decimal prix, decimal stock, int count, string sessionId = "cs_test_1")
        {
            var categorie = new Category { Nom = "Cat", Image = "c.jpg", Disponible = true };
            _context.Categories.Add(categorie);

            var produit = new Produit
            {
                Nom = "Creme",
                Categorie = categorie,
                Prix = prix,
                Stock = stock,
                Disponible = true,
                Image = "p.jpg",
                RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
            };
            _context.Produits.Add(produit);
            _context.SaveChanges();

            var order = new OrderHeader
            {
                ApplicationUserId = UserId,
                OrderDate = DateTime.UtcNow,
                OrderTotal = prix * count,
                Subtotal = prix * count,
                OrderStatus = SD.StatusPending,
                PaymentStatus = SD.PaymentStatusPending,
                SessionId = sessionId,
                Name = "Alice",
                PhoneNumber = "5145551234",
                StreetAddress = "1 rue Test",
                City = "Montreal",
                State = "QC",
                PostalCode = "H0H0H0",
            };
            order.OrderDetails.Add(new OrderDetail { ProduitId = produit.ProduitId, Count = count, Price = prix });
            _context.OrderHeaders.Add(order);
            _context.SaveChanges();

            _context.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = UserId, ProduitId = produit.ProduitId, Count = count });
            _context.SaveChanges();

            return (order.Id, produit.ProduitId);
        }

        private static Session PaidSession(string sessionId, int orderId, long amountTotal, string currency = "cad", string paymentIntentId = "pi_test")
            => new()
            {
                Id = sessionId,
                PaymentStatus = "paid",
                AmountTotal = amountTotal,
                Currency = currency,
                PaymentIntentId = paymentIntentId,
                Metadata = new Dictionary<string, string> { ["OrderId"] = orderId.ToString() },
            };

        [Fact]
        public async Task DuplicateEvent_FastPath_ProducesNoSecondEffect()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 1);
            var session = PaidSession("cs_test_1", orderId, 2500);

            var first = await _sut.ProcessCheckoutSessionEventAsync("evt_123", "checkout.session.completed", session);
            Assert.Equal(FulfillmentOutcome.Fulfilled, first.Outcome);

            var second = await _sut.ProcessCheckoutSessionEventAsync("evt_123", "checkout.session.completed", session);
            Assert.Equal(FulfillmentOutcome.AlreadyProcessed, second.Outcome);

            var stock = _context.Produits.AsNoTracking().Single(p => p.ProduitId == produitId).Stock;
            Assert.Equal(9, stock);
            Assert.Equal(1, _context.ProcessedStripeEvents.Count(e => e.StripeEventId == "evt_123"));
        }

        [Fact]
        public async Task AlreadyPaidOrder_SecondDistinctEventId_IsIgnored_NoDoubleFulfillment()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 1);
            var session = PaidSession("cs_test_1", orderId, 2500);

            await _sut.ProcessCheckoutSessionEventAsync("evt_first", "checkout.session.completed", session);

            // Deuxième événement STRIPE DIFFÉRENT pour la même commande déjà payée
            // (section 23) : ne doit jamais redécrémenter le stock.
            var result = await _sut.ProcessCheckoutSessionEventAsync("evt_second_distinct", "checkout.session.async_payment_succeeded", session);

            Assert.Equal(FulfillmentOutcome.AlreadyProcessed, result.Outcome);
            var stock = _context.Produits.AsNoTracking().Single(p => p.ProduitId == produitId).Stock;
            Assert.Equal(9, stock);
        }

        [Fact]
        public async Task AmountMismatch_DoesNotFulfill_OrderStaysPending()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 1);
            var session = PaidSession("cs_test_1", orderId, amountTotal: 100); // 1.00 CAD au lieu de 25.00 CAD

            var result = await _sut.ProcessCheckoutSessionEventAsync("evt_amount", "checkout.session.completed", session);

            Assert.Equal(FulfillmentOutcome.AmountMismatch, result.Outcome);
            var order = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == orderId);
            Assert.Equal(SD.PaymentStatusPending, order.PaymentStatus);
            var stock = _context.Produits.AsNoTracking().Single(p => p.ProduitId == produitId).Stock;
            Assert.Equal(10, stock);
        }

        [Fact]
        public async Task CurrencyMismatch_DoesNotFulfill()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 1);
            var session = PaidSession("cs_test_1", orderId, amountTotal: 2500, currency: "usd");

            var result = await _sut.ProcessCheckoutSessionEventAsync("evt_currency", "checkout.session.completed", session);

            Assert.Equal(FulfillmentOutcome.CurrencyMismatch, result.Outcome);
            var stock = _context.Produits.AsNoTracking().Single(p => p.ProduitId == produitId).Stock;
            Assert.Equal(10, stock);
        }

        [Fact]
        public async Task StockInsufficientAtFulfillmentTime_MarksPaidButLeavesOrderPending_ForManualRemediation()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 0, count: 1);
            var session = PaidSession("cs_test_1", orderId, 2500);

            var result = await _sut.ProcessCheckoutSessionEventAsync("evt_stock", "checkout.session.completed", session);

            Assert.Equal(FulfillmentOutcome.StockUnavailable, result.Outcome);
            var order = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == orderId);
            // Le paiement a réellement eu lieu : reconnu comme tel. OrderStatus reste
            // Pending (pas Processing) : c'est le signal explicite de remédiation
            // (section 20), pas un état caché.
            Assert.Equal(SD.PaymentStatusApproved, order.PaymentStatus);
            Assert.Equal(SD.StatusPending, order.OrderStatus);
            var stock = _context.Produits.AsNoTracking().Single(p => p.ProduitId == produitId).Stock;
            Assert.Equal(0, stock);
        }

        [Fact]
        public async Task PaymentFailedEvent_CancelsOrder_NoStockChange()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 1);
            var session = PaidSession("cs_test_1", orderId, 2500);
            session.PaymentStatus = "unpaid";

            var result = await _sut.ProcessCheckoutSessionEventAsync("evt_failed", "checkout.session.async_payment_failed", session);

            Assert.Equal(FulfillmentOutcome.PaymentFailed, result.Outcome);
            var order = _context.OrderHeaders.AsNoTracking().Single(o => o.Id == orderId);
            Assert.Equal(SD.PaymentStatusRejected, order.PaymentStatus);
            Assert.Equal(SD.StatusCancelled, order.OrderStatus);
            var stock = _context.Produits.AsNoTracking().Single(p => p.ProduitId == produitId).Stock;
            Assert.Equal(10, stock);
        }

        [Fact]
        public async Task OrderNotFound_ReturnsOrderNotFound_NoException()
        {
            var session = PaidSession("cs_test_missing", orderId: 999999, amountTotal: 2500);

            var result = await _sut.ProcessCheckoutSessionEventAsync("evt_missing", "checkout.session.completed", session);

            Assert.Equal(FulfillmentOutcome.OrderNotFound, result.Outcome);
        }

        [Fact]
        public async Task SessionIdMismatch_IsTreatedAsOrderNotFound()
        {
            var (orderId, _) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 1, sessionId: "cs_real_session");
            // Un événement dont le SessionId ne correspond pas à celui stocké côté
            // serveur ne doit jamais être traité comme légitime pour cette commande.
            var session = PaidSession("cs_attacker_supplied", orderId, 2500);

            var result = await _sut.ProcessCheckoutSessionEventAsync("evt_mismatch", "checkout.session.completed", session);

            Assert.Equal(FulfillmentOutcome.OrderNotFound, result.Outcome);
        }

        [Fact]
        public async Task SuccessfulFulfillment_DecrementsStock_MarksOrderProcessing_ClearsCart_SnapshotsProductName()
        {
            var (orderId, produitId) = SeedPendingOrder(prix: 25.00m, stock: 10, count: 2);
            var session = PaidSession("cs_test_1", orderId, 5000);

            var result = await _sut.ProcessCheckoutSessionEventAsync("evt_ok", "checkout.session.completed", session);

            Assert.Equal(FulfillmentOutcome.Fulfilled, result.Outcome);

            var order = _context.OrderHeaders
                .Include(o => o.OrderDetails)
                .AsNoTracking()
                .Single(o => o.Id == orderId);
            Assert.Equal(SD.PaymentStatusApproved, order.PaymentStatus);
            Assert.Equal(SD.StatusInProcess, order.OrderStatus);
            Assert.Equal("pi_test", order.PaymentIntentId);
            Assert.Equal("Creme", order.OrderDetails.Single().ProduitNom);

            var stock = _context.Produits.AsNoTracking().Single(p => p.ProduitId == produitId).Stock;
            Assert.Equal(8, stock);

            var cartRemaining = _context.ShoppingCarts.Count(c => c.ApplicationUserId == UserId);
            Assert.Equal(0, cartRemaining);

            var processed = _context.ProcessedStripeEvents.AsNoTracking().Single(e => e.StripeEventId == "evt_ok");
            Assert.Equal("Processed", processed.ProcessingStatus);
            Assert.Equal(orderId, processed.OrderId);
        }

        [Fact]
        public async Task UnsupportedEventForCheckoutSession_NoOrderId_ReturnsOrderNotFound_NoException()
        {
            var session = new Session { Id = "cs_test_orphan", PaymentStatus = "paid", AmountTotal = 100, Currency = "cad", Metadata = new Dictionary<string, string>() };

            var result = await _sut.ProcessCheckoutSessionEventAsync("evt_no_metadata", "checkout.session.completed", session);

            Assert.Equal(FulfillmentOutcome.OrderNotFound, result.Outcome);
        }

        public void Dispose() => _context.Dispose();
    }
}
