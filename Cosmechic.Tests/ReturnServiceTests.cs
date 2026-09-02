using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 69) : ownership, quantité, retour partiel.
    // COSMECHIC-BUSINESS-POLICY-001 : fenêtre de retour de 30 jours (voir ReturnWindowTests.cs
    // pour la matrice dédiée 29/30/31 jours) — désactivée ici (ReturnWindowDays=null) pour ne
    // pas coupler ces tests pré-existants, qui utilisent des commandes sans ShippedAt/
    // DeliveredAt, à une politique qui leur est étrangère.
    public class ReturnServiceTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly ReturnService _sut;

        public ReturnServiceTests()
        {
            var policyOptions = Options.Create(new CommercePolicyOptions { ReturnWindowDays = null });
            _sut = new ReturnService(_context, new OrderLifecycleService(_context), policyOptions);
        }

        private (OrderHeader Order, OrderDetail DetailA, OrderDetail DetailB) SeedShippedOrderWithTwoLines(string ownerId = "user-a")
        {
            var categorie = new Category { Nom = "Cat", Image = "c.jpg", Disponible = true };
            _context.Categories.Add(categorie);
            _context.SaveChanges();

            var produitA = new Produit { Nom = "A", CategorieId = categorie.CategorieId, Prix = 10m, Stock = 5, Disponible = true, Image = "a.jpg", RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 } };
            var produitB = new Produit { Nom = "B", CategorieId = categorie.CategorieId, Prix = 20m, Stock = 5, Disponible = true, Image = "b.jpg", RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 } };
            _context.Produits.AddRange(produitA, produitB);
            _context.SaveChanges();

            var order = new OrderHeader
            {
                ApplicationUserId = ownerId,
                OrderDate = DateTime.UtcNow,
                OrderTotal = 40m,
                Subtotal = 40m,
                OrderStatus = SD.OrderStatusConfirmed,
                PaymentStatus = SD.PaymentStatusPaid,
                FulfillmentStatus = SD.FulfillmentStatusShipped,
                Name = "Test",
                PhoneNumber = "5145551234",
                StreetAddress = "1 rue Test",
                City = "Montreal",
                State = "QC",
                PostalCode = "H0H0H0",
            };
            _context.OrderHeaders.Add(order);
            _context.SaveChanges();

            var detailA = new OrderDetail { OrderHeaderId = order.Id, ProduitId = produitA.ProduitId, Count = 2, Price = 10m, ProduitNom = "A" };
            var detailB = new OrderDetail { OrderHeaderId = order.Id, ProduitId = produitB.ProduitId, Count = 1, Price = 20m, ProduitNom = "B" };
            _context.OrderDetails.AddRange(detailA, detailB);
            _context.SaveChanges();

            return (order, detailA, detailB);
        }

        [Fact]
        public async Task ForeignOrder_CreateReturnRequest_IsDenied()
        {
            var (order, detailA, _) = SeedShippedOrderWithTwoLines(ownerId: "user-a");

            var result = await _sut.CreateReturnRequestAsync(
                order.Id, "user-b", "reason", null, new[] { new ReturnItemInput(detailA.Id, 1, null, ReturnReasonCategory.DefectOrNonConformity, null, null, null) });

            Assert.IsType<ReturnRequestRejectedByPolicy>(result);
            Assert.Empty(_context.ReturnRequests);
        }

        [Fact]
        public async Task EligibleOwnOrder_CreateReturnRequest_IsAccepted()
        {
            var (order, detailA, _) = SeedShippedOrderWithTwoLines();

            var result = await _sut.CreateReturnRequestAsync(
                order.Id, "user-a", "damaged", "please refund", new[] { new ReturnItemInput(detailA.Id, 1, "damaged", ReturnReasonCategory.DefectOrNonConformity, null, null, null) });

            var created = Assert.IsType<ReturnRequestCreated>(result);
            var returnRequest = _context.ReturnRequests.Include(rr => rr.Items).Single(rr => rr.Id == created.ReturnRequestId);
            Assert.Equal(SD.ReturnStatusRequested, returnRequest.Status);
            Assert.Single(returnRequest.Items);
            Assert.Equal(1, returnRequest.Items.Single().Quantity);
        }

        [Fact]
        public async Task PartialReturn_LeavesRemainingQuantityReturnable()
        {
            var (order, detailA, _) = SeedShippedOrderWithTwoLines();
            // detailA purchased Count=2, return only 1.
            await _sut.CreateReturnRequestAsync(order.Id, "user-a", null, null, new[] { new ReturnItemInput(detailA.Id, 1, null, ReturnReasonCategory.DefectOrNonConformity, null, null, null) });

            var reloaded = await _context.OrderDetails.FirstAsync(d => d.Id == detailA.Id);
            var eligibility = await _sut.CanRequestReturnAsync(reloaded, 1, ReturnReasonCategory.DefectOrNonConformity, null, null, null);

            Assert.IsType<ReturnEligible>(eligibility);
        }

        [Fact]
        public async Task QuantityZero_IsRejected()
        {
            var (order, detailA, _) = SeedShippedOrderWithTwoLines();
            var reloaded = await _context.OrderDetails.Include(d => d.OrderHeader).FirstAsync(d => d.Id == detailA.Id);

            var eligibility = await _sut.CanRequestReturnAsync(reloaded, 0, ReturnReasonCategory.DefectOrNonConformity, null, null, null);

            Assert.IsType<ReturnIneligible>(eligibility);
        }

        [Fact]
        public async Task QuantityExceedingPurchased_IsRejected()
        {
            var (order, detailA, _) = SeedShippedOrderWithTwoLines();
            var reloaded = await _context.OrderDetails.Include(d => d.OrderHeader).FirstAsync(d => d.Id == detailA.Id);

            // detailA.Count == 2 ; demander 3 doit être refusé.
            var eligibility = await _sut.CanRequestReturnAsync(reloaded, 3, ReturnReasonCategory.DefectOrNonConformity, null, null, null);

            Assert.IsType<ReturnIneligible>(eligibility);
        }

        [Fact]
        public async Task DuplicateExcessiveReturn_AcrossTwoRequests_IsRejected()
        {
            var (order, detailA, _) = SeedShippedOrderWithTwoLines();
            // detailA.Count == 2. First request claims 2 (all of it).
            var first = await _sut.CreateReturnRequestAsync(order.Id, "user-a", null, null, new[] { new ReturnItemInput(detailA.Id, 2, null, ReturnReasonCategory.DefectOrNonConformity, null, null, null) });
            Assert.IsType<ReturnRequestCreated>(first);

            // Second request for the same line, even 1 more unit, must be rejected —
            // nothing left to return.
            var second = await _sut.CreateReturnRequestAsync(order.Id, "user-a", null, null, new[] { new ReturnItemInput(detailA.Id, 1, null, ReturnReasonCategory.DefectOrNonConformity, null, null, null) });

            Assert.IsType<ReturnRequestRejectedByPolicy>(second);
        }

        [Fact]
        public async Task UnshippedOrder_ReturnRequest_IsRejected_CancellationIsTheCorrectPath()
        {
            var (order, detailA, _) = SeedShippedOrderWithTwoLines();
            var orderEntity = await _context.OrderHeaders.FirstAsync(o => o.Id == order.Id);
            orderEntity.FulfillmentStatus = SD.FulfillmentStatusUnfulfilled;
            await _context.SaveChangesAsync();

            var result = await _sut.CreateReturnRequestAsync(order.Id, "user-a", null, null, new[] { new ReturnItemInput(detailA.Id, 1, null, ReturnReasonCategory.DefectOrNonConformity, null, null, null) });

            Assert.IsType<ReturnRequestRejectedByPolicy>(result);
        }

        [Fact]
        public async Task ApproveThenRejectSameRequest_TransitionIsRejected()
        {
            var (order, detailA, _) = SeedShippedOrderWithTwoLines();
            var created = (ReturnRequestCreated)await _sut.CreateReturnRequestAsync(
                order.Id, "user-a", null, null, new[] { new ReturnItemInput(detailA.Id, 1, null, ReturnReasonCategory.DefectOrNonConformity, null, null, null) });

            var approveResult = await _sut.ApproveAsync(created.ReturnRequestId, "admin-1", "ok");
            Assert.IsType<ReturnActionApplied>(approveResult);

            // Approved -> Rejected n'est pas une transition valide (seul Requested -> Rejected l'est).
            var rejectResult = await _sut.RejectAsync(created.ReturnRequestId, "admin-1", "changed mind");
            Assert.IsType<ReturnActionRejected>(rejectResult);
        }

        [Fact]
        public async Task FullLifecycle_RequestedToCompleted_Succeeds()
        {
            var (order, detailA, _) = SeedShippedOrderWithTwoLines();
            var created = (ReturnRequestCreated)await _sut.CreateReturnRequestAsync(
                order.Id, "user-a", null, null, new[] { new ReturnItemInput(detailA.Id, 1, null, ReturnReasonCategory.DefectOrNonConformity, null, null, null) });

            Assert.IsType<ReturnActionApplied>(await _sut.ApproveAsync(created.ReturnRequestId, "admin-1", null));
            Assert.IsType<ReturnActionApplied>(await _sut.MarkReceivedAsync(created.ReturnRequestId, "admin-1"));
            Assert.IsType<ReturnActionApplied>(await _sut.CompleteAsync(created.ReturnRequestId, "admin-1"));

            var reloaded = _context.ReturnRequests.AsNoTracking().Single(rr => rr.Id == created.ReturnRequestId);
            Assert.Equal(SD.ReturnStatusCompleted, reloaded.Status);
            Assert.NotNull(reloaded.ApprovedAt);
            Assert.NotNull(reloaded.ReceivedAt);
            Assert.NotNull(reloaded.CompletedAt);
        }

        public void Dispose() => _context.Dispose();
    }
}
