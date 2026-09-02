using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 17 A-D) : matrice de tests de la
    // décision PM sur la politique de retour — la fenêtre de 30 jours et les déclarations
    // d'état ne gouvernent que ChangeOfMind ; Defect/WrongItem/Safety en sont indépendants ;
    // une ligne SafetyOrAdverseReaction route toute la demande vers NeedsSafetyReview.
    public class ReturnPolicyImplementationTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly ReturnService _sut;

        public ReturnPolicyImplementationTests()
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

        // ============================================================
        // A. CHANGE OF MIND
        // ============================================================

        [Fact]
        public async Task ChangeOfMind_Opened_IsRejected()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.ChangeOfMind, isOpened: true, isUsed: false, customerDeclaredResellable: true);

            Assert.IsType<ReturnIneligible>(eligibility);
        }

        [Fact]
        public async Task ChangeOfMind_Used_IsRejected()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.ChangeOfMind, isOpened: false, isUsed: true, customerDeclaredResellable: true);

            Assert.IsType<ReturnIneligible>(eligibility);
        }

        [Fact]
        public async Task ChangeOfMind_NotResellable_IsRejected()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.ChangeOfMind, isOpened: false, isUsed: false, customerDeclaredResellable: false);

            Assert.IsType<ReturnIneligible>(eligibility);
        }

        [Fact]
        public async Task ChangeOfMind_ValidCombination_IsAccepted()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.ChangeOfMind, isOpened: false, isUsed: false, customerDeclaredResellable: true);

            Assert.IsType<ReturnEligible>(eligibility);
        }

        [Fact]
        public async Task ChangeOfMind_ConditionFieldsOmitted_IsRejected()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.ChangeOfMind, isOpened: null, isUsed: null, customerDeclaredResellable: null);

            Assert.IsType<ReturnIneligible>(eligibility);
        }

        [Fact]
        public async Task CreateReturnRequest_ChangeOfMind_ClientTriesToForceOpenedFalseAndDeclareResellable_OnlyDtoFieldsApply()
        {
            // Preuve d'overposting côté service (section 17A) : ReturnItemInput est un DTO
            // étroit — il n'existe structurellement aucun champ "Status"/"Restocked"/montant
            // financier qu'un appelant pourrait fournir en plus. Ici, on vérifie que fournir
            // exactement les 4 champs attendus (et rien d'autre, par construction du type)
            // suffit à déterminer le résultat, sans qu'aucun état caché ne puisse influencer
            // l'issue.
            var (order, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));

            var result = await _sut.CreateReturnRequestAsync(
                order.Id, "user-a", null, null,
                new[] { new ReturnItemInput(detail.Id, 1, null, ReturnReasonCategory.ChangeOfMind, false, false, true) });

            var created = Assert.IsType<ReturnRequestCreated>(result);
            var item = _context.ReturnItems.Single(ri => ri.ReturnRequestId == created.ReturnRequestId);
            Assert.False(item.Restocked);
            Assert.Null(item.RestockedAt);
        }

        // ============================================================
        // B. DEFECT / NON-CONFORMITY
        // ============================================================

        [Fact]
        public async Task DefectOrNonConformity_OpenedAndUsed_IsNotAutoRejected()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.DefectOrNonConformity, isOpened: true, isUsed: true, customerDeclaredResellable: false);

            Assert.IsType<ReturnEligible>(eligibility);
        }

        [Fact]
        public async Task DefectOrNonConformity_Beyond30Days_IsNotRejectedByWindowAlone()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-90));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.DefectOrNonConformity, isOpened: null, isUsed: null, customerDeclaredResellable: null);

            Assert.IsType<ReturnEligible>(eligibility);
        }

        // ============================================================
        // C. WRONG ITEM / MERCHANT FAULT
        // ============================================================

        [Fact]
        public async Task WrongItemOrMerchantFault_Opened_IsNotRejectedByChangeOfMindRestriction()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.WrongItemOrMerchantFault, isOpened: true, isUsed: true, customerDeclaredResellable: false);

            Assert.IsType<ReturnEligible>(eligibility);
        }

        [Fact]
        public async Task WrongItemOrMerchantFault_Beyond30Days_IsNotRejectedByWindow()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-60));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.WrongItemOrMerchantFault, isOpened: null, isUsed: null, customerDeclaredResellable: null);

            Assert.IsType<ReturnEligible>(eligibility);
        }

        [Fact]
        public async Task WrongItemOrMerchantFault_KnownAtRequestCreation_PersistsOnItem()
        {
            var (order, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));

            var result = await _sut.CreateReturnRequestAsync(
                order.Id, "user-a", null, null,
                new[] { new ReturnItemInput(detail.Id, 1, "mauvais article", ReturnReasonCategory.WrongItemOrMerchantFault, null, null, null) });

            var created = Assert.IsType<ReturnRequestCreated>(result);
            var item = _context.ReturnItems.Single(ri => ri.ReturnRequestId == created.ReturnRequestId);
            Assert.Equal(ReturnReasonCategory.WrongItemOrMerchantFault, item.Category);
        }

        // ============================================================
        // D. SAFETY / ADVERSE REACTION
        // ============================================================

        [Fact]
        public async Task SafetyOrAdverseReaction_Opened_IsNotAutoRejected()
        {
            var (_, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));

            var eligibility = await _sut.CanRequestReturnAsync(
                detail, 1, ReturnReasonCategory.SafetyOrAdverseReaction, isOpened: true, isUsed: true, customerDeclaredResellable: null);

            Assert.IsType<ReturnEligible>(eligibility);
        }

        [Fact]
        public async Task CreateReturnRequest_WithSafetyItem_StartsInNeedsSafetyReview_NeverChangeOfMindPath()
        {
            var (order, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));

            var result = await _sut.CreateReturnRequestAsync(
                order.Id, "user-a", null, null,
                new[] { new ReturnItemInput(detail.Id, 1, "réaction cutanée", ReturnReasonCategory.SafetyOrAdverseReaction, null, null, null) });

            var created = Assert.IsType<ReturnRequestCreated>(result);
            var returnRequest = _context.ReturnRequests.Single(rr => rr.Id == created.ReturnRequestId);
            Assert.Equal(SD.ReturnStatusNeedsSafetyReview, returnRequest.Status);
        }

        [Fact]
        public async Task CreateReturnRequest_WithoutSafetyItem_StartsInRequested()
        {
            var (order, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));

            var result = await _sut.CreateReturnRequestAsync(
                order.Id, "user-a", null, null,
                new[] { new ReturnItemInput(detail.Id, 1, null, ReturnReasonCategory.DefectOrNonConformity, null, null, null) });

            var created = Assert.IsType<ReturnRequestCreated>(result);
            var returnRequest = _context.ReturnRequests.Single(rr => rr.Id == created.ReturnRequestId);
            Assert.Equal(SD.ReturnStatusRequested, returnRequest.Status);
        }

        [Fact]
        public async Task NeedsSafetyReview_CannotBeApprovedDirectly_ClientCannotAutoApproveOrRefund()
        {
            var (order, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));
            var created = (ReturnRequestCreated)await _sut.CreateReturnRequestAsync(
                order.Id, "user-a", null, null,
                new[] { new ReturnItemInput(detail.Id, 1, null, ReturnReasonCategory.SafetyOrAdverseReaction, null, null, null) });

            // Approve/Reject directement depuis NeedsSafetyReview doivent être refusés — seule
            // ReleaseSafetyReviewAsync (admin) peut faire sortir la demande de cette voie.
            var approveResult = await _sut.ApproveAsync(created.ReturnRequestId, "admin-1", null);
            Assert.IsType<ReturnActionRejected>(approveResult);

            var rejectResult = await _sut.RejectAsync(created.ReturnRequestId, "admin-1", null);
            Assert.IsType<ReturnActionRejected>(rejectResult);
        }

        [Fact]
        public async Task NeedsSafetyReview_ReleasedByAdmin_TransitionsToRequested_AuditedViaHistory()
        {
            var (order, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));
            var created = (ReturnRequestCreated)await _sut.CreateReturnRequestAsync(
                order.Id, "user-a", null, null,
                new[] { new ReturnItemInput(detail.Id, 1, null, ReturnReasonCategory.SafetyOrAdverseReaction, null, null, null) });

            var releaseResult = await _sut.ReleaseSafetyReviewAsync(created.ReturnRequestId, "admin-1", "revu, rien de grave");

            Assert.IsType<ReturnActionApplied>(releaseResult);
            var returnRequest = _context.ReturnRequests.AsNoTracking().Single(rr => rr.Id == created.ReturnRequestId);
            Assert.Equal(SD.ReturnStatusRequested, returnRequest.Status);

            // Audité : au moins un événement d'historique de statut existe pour cette
            // transition (lifecycleService.RecordEvent, même mécanisme que toute autre
            // transition de commande).
            var historyExists = _context.OrderStatusHistories.Any(h => h.OrderId == order.Id && h.NewStatus == SD.ReturnStatusRequested);
            Assert.True(historyExists);
        }

        [Fact]
        public async Task ReleaseSafetyReview_ThenApprove_Succeeds()
        {
            var (order, detail) = SeedDeliveredOrder(DateTime.UtcNow.AddDays(-5));
            var created = (ReturnRequestCreated)await _sut.CreateReturnRequestAsync(
                order.Id, "user-a", null, null,
                new[] { new ReturnItemInput(detail.Id, 1, null, ReturnReasonCategory.SafetyOrAdverseReaction, null, null, null) });

            Assert.IsType<ReturnActionApplied>(await _sut.ReleaseSafetyReviewAsync(created.ReturnRequestId, "admin-1", null));
            Assert.IsType<ReturnActionApplied>(await _sut.ApproveAsync(created.ReturnRequestId, "admin-1", null));

            var returnRequest = _context.ReturnRequests.AsNoTracking().Single(rr => rr.Id == created.ReturnRequestId);
            Assert.Equal(SD.ReturnStatusApproved, returnRequest.Status);
        }

        public void Dispose() => _context.Dispose();
    }
}
