using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-BUSINESS-POLICY-001 (section 3) : RETURN_WINDOW_DAYS=30, approuvé par le PM.
    // Matrice de frontière explicite : le jour 30 complet reste éligible (convention "30
    // jours pleins"), le jour 31 ne l'est plus. Compté depuis DeliveredAt si disponible,
    // sinon ShippedAt (voir ReturnService.CanRequestReturnAsync).
    public class ReturnWindowTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly ReturnService _sut;

        public ReturnWindowTests()
        {
            var policyOptions = Options.Create(new CommercePolicyOptions { ReturnWindowDays = 30 });
            _sut = new ReturnService(_context, new OrderLifecycleService(_context), policyOptions);
        }

        private (OrderHeader Order, OrderDetail Detail) SeedDeliveredOrder(DateTime deliveredAt, string ownerId = "user-a")
        {
            var categorie = new Category { Nom = "Cat", Image = "c.jpg", Disponible = true };
            _context.Categories.Add(categorie);
            _context.SaveChanges();

            var produit = new Produit { Nom = "A", CategorieId = categorie.CategorieId, Prix = 10m, Stock = 5, Disponible = true, Image = "a.jpg", RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 } };
            _context.Produits.Add(produit);
            _context.SaveChanges();

            var order = new OrderHeader
            {
                ApplicationUserId = ownerId,
                OrderDate = deliveredAt.AddDays(-3),
                OrderTotal = 10m,
                Subtotal = 10m,
                OrderStatus = SD.OrderStatusCompleted,
                PaymentStatus = SD.PaymentStatusPaid,
                FulfillmentStatus = SD.FulfillmentStatusDelivered,
                ShippedAt = deliveredAt.AddDays(-2),
                DeliveredAt = deliveredAt,
                Name = "Test",
                PhoneNumber = "5145551234",
                StreetAddress = "1 rue Test",
                City = "Montreal",
                State = "QC",
                PostalCode = "H0H0H0",
            };
            _context.OrderHeaders.Add(order);
            _context.SaveChanges();

            var detail = new OrderDetail { OrderHeaderId = order.Id, ProduitId = produit.ProduitId, Count = 1, Price = 10m, ProduitNom = "A" };
            _context.OrderDetails.Add(detail);
            _context.SaveChanges();

            return (order, detail);
        }

        [Fact]
        public async Task DeliveredExactly29DaysAgo_IsEligible()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-29));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.ChangeOfMind, isOpened: false, isUsed: false, customerDeclaredResellable: true);

            Assert.IsType<ReturnEligible>(eligibility);
        }

        [Fact]
        public async Task DeliveredExactly30DaysAgo_IsEligible_FrontierIsInclusive()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-30));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.ChangeOfMind, isOpened: false, isUsed: false, customerDeclaredResellable: true);

            Assert.IsType<ReturnEligible>(eligibility);
        }

        [Fact]
        public async Task DeliveredExactly31DaysAgo_IsIneligible()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-31));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.ChangeOfMind, isOpened: false, isUsed: false, customerDeclaredResellable: true);

            Assert.IsType<ReturnIneligible>(eligibility);
        }

        // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 4) : décision PM appliquée — la
        // fenêtre de 30 jours ne gouverne QUE ChangeOfMind. Une demande DefectOrNonConformity
        // au-delà de 30 jours n'est jamais rejetée par la seule fenêtre commerciale.
        [Fact]
        public async Task DeliveredExactly31DaysAgo_DefectOrNonConformity_IsNotRejectedByWindow()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-31));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.DefectOrNonConformity, isOpened: null, isUsed: null, customerDeclaredResellable: null);

            Assert.IsType<ReturnEligible>(eligibility);
        }

        [Fact]
        public async Task UndeliveredUnshippedOrder_IsGuardedByFulfillmentStatus_NotWindow()
        {
            var (order, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));
            var reloaded = await _context.OrderHeaders.FindAsync(order.Id);
            reloaded!.FulfillmentStatus = SD.FulfillmentStatusUnfulfilled;
            reloaded.ShippedAt = null;
            reloaded.DeliveredAt = null;
            await _context.SaveChangesAsync();

            var reloadedDetail = await _context.OrderDetails.FindAsync(detail.Id);
            var eligibility = await _sut.CanRequestReturnAsync(
                reloadedDetail!, 1, ReturnReasonCategory.ChangeOfMind, isOpened: false, isUsed: false, customerDeclaredResellable: true);

            Assert.IsType<ReturnIneligible>(eligibility);
        }

        [Fact]
        public async Task ForeignOwner_CreateReturnRequest_IsDeniedRegardlessOfWindow()
        {
            var (order, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5), ownerId: "user-a");

            var result = await _sut.CreateReturnRequestAsync(
                order.Id, "user-b", "reason", null,
                new[] { new ReturnItemInput(detail.Id, 1, null, ReturnReasonCategory.ChangeOfMind, false, false, true) });

            Assert.IsType<ReturnRequestRejectedByPolicy>(result);
        }

        public void Dispose() => _context.Dispose();
    }
}
