using Cosmechic.Data;
using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Cosmechic.Tests
{
    // COSMECHIC-BUSINESS-POLICY-001 (section 4/5/11) : RequestReturnRefundAsync touche des
    // invariants métier réels (CK_Refunds_Breakdown_Equals_Amount, plafond du solde
    // remboursable, non-dépassement de TaxAmount original) — vérifié contre SQL Server réel,
    // pas InMemory (mêmes garanties de rigueur que SqlServerRefundAndRestockConcurrencyTests).
    [Collection("SqlServerFixture collection")]
    public class SqlServerReturnRefundPolicyTests
    {
        private readonly SqlServerFixture _fixture;
        private readonly ITestOutputHelper _output;

        public SqlServerReturnRefundPolicyTests(SqlServerFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        private bool SkipIfUnavailable()
        {
            if (_fixture.IsAvailable)
            {
                return false;
            }

            _output.WriteLine($"Test ignoré (SQL Server jetable indisponible) : {_fixture.SkipReason}");
            return true;
        }

        private async Task<string> SeedUserAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_fixture.ConnectionString)
                .Options;
            await using var identityContext = new ApplicationDbContext(options);
            var userId = $"user-{Guid.NewGuid():N}";
            identityContext.Users.Add(new IdentityUser { Id = userId, UserName = userId });
            await identityContext.SaveChangesAsync();
            return userId;
        }

        // Subtotal=100 (A=60/B=40), ShippingAmount=15, TaxAmount=5 (5%), OrderTotal=120.
        private async Task<(int OrderId, int DetailAId, int DetailBId)> SeedOrderWithTwoLinesAsync(CosmechicsContext context)
        {
            var category = new Category { Nom = $"Cat-{Guid.NewGuid():N}", Image = "c.jpg", Disponible = true };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var produitA = new Produit { Nom = $"A-{Guid.NewGuid():N}", CategorieId = category.CategorieId, Prix = 60m, Stock = 5, Disponible = true, Image = "a.jpg" };
            var produitB = new Produit { Nom = $"B-{Guid.NewGuid():N}", CategorieId = category.CategorieId, Prix = 40m, Stock = 5, Disponible = true, Image = "b.jpg" };
            context.Produits.AddRange(produitA, produitB);
            await context.SaveChangesAsync();

            var userId = await SeedUserAsync();
            var order = new OrderHeader
            {
                ApplicationUserId = userId,
                OrderDate = DateTime.UtcNow,
                OrderTotal = 120m,
                Subtotal = 100m,
                ShippingAmount = 15m,
                TaxAmount = 5m,
                OrderStatus = SD.OrderStatusConfirmed,
                PaymentStatus = SD.PaymentStatusPaid,
                FulfillmentStatus = SD.FulfillmentStatusDelivered,
                PaymentIntentId = "pi_test",
                Name = "Test",
                PhoneNumber = "5145551234",
                StreetAddress = "1 rue Test",
                City = "Montreal",
                State = "QC",
                PostalCode = "H0H0H0",
            };
            context.OrderHeaders.Add(order);
            await context.SaveChangesAsync();

            var detailA = new OrderDetail { OrderHeaderId = order.Id, ProduitId = produitA.ProduitId, Count = 1, Price = 60m, ProduitNom = "A" };
            var detailB = new OrderDetail { OrderHeaderId = order.Id, ProduitId = produitB.ProduitId, Count = 1, Price = 40m, ProduitNom = "B" };
            context.OrderDetails.AddRange(detailA, detailB);
            await context.SaveChangesAsync();

            return (order.Id, detailA.Id, detailB.Id);
        }

        // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 8) : category détermine
        // désormais la RefundCause dérivée côté serveur par RequestReturnRefundAsync — plus
        // aucun appelant ne peut la fournir directement.
        private static async Task<int> SeedCompletedReturnAsync(
            CosmechicsContext context, int orderId, int orderDetailId, int quantity, ReturnReasonCategory category)
        {
            var returnRequest = new ReturnRequest
            {
                OrderId = orderId,
                ApplicationUserId = "irrelevant",
                Status = SD.ReturnStatusCompleted,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
            };
            context.ReturnRequests.Add(returnRequest);
            await context.SaveChangesAsync();

            context.ReturnItems.Add(new ReturnItem
            {
                ReturnRequestId = returnRequest.Id,
                OrderDetailId = orderDetailId,
                Quantity = quantity,
                Category = category,
            });
            await context.SaveChangesAsync();

            return returnRequest.Id;
        }

        // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 14/18) : preuve directe que la
        // migration AddReturnReasonCategory backfill correctement une ligne ReturnItem
        // "historique" (insérée sans spécifier Category, exactement ce qui se produirait pour
        // une ligne déjà en base au moment où AddColumn s'exécute) vers LegacyUnclassified —
        // jamais requalifiée arbitrairement en ChangeOfMind. Utilise la contrainte DEFAULT SQL
        // réellement posée par la migration (ExecuteSqlRaw, colonne omise), pas une valeur
        // choisie côté C#.
        [Fact]
        public async Task HistoricalReturnItem_InsertedWithoutCategory_BackfillsToLegacyUnclassified_NeverChangeOfMind()
        {
            if (SkipIfUnavailable()) return;

            using var setupContext = _fixture.CreateBusinessContext();
            var (orderId, detailAId, _) = await SeedOrderWithTwoLinesAsync(setupContext);

            var returnRequest = new ReturnRequest
            {
                OrderId = orderId,
                ApplicationUserId = "irrelevant",
                Status = SD.ReturnStatusRequested,
                CreatedAt = DateTime.UtcNow,
            };
            setupContext.ReturnRequests.Add(returnRequest);
            await setupContext.SaveChangesAsync();

            // Colonne Category volontairement omise — reproduit exactement l'état d'une ligne
            // pré-existante au moment de l'exécution de la migration AddColumn.
            await setupContext.Database.ExecuteSqlRawAsync(
                "INSERT INTO [ReturnItems] ([ReturnRequestId], [OrderDetailId], [Quantity], [Restocked]) VALUES ({0}, {1}, {2}, 0)",
                returnRequest.Id, detailAId, 1);

            using var verify = _fixture.CreateBusinessContext();
            var item = await verify.ReturnItems.AsNoTracking().SingleAsync(ri => ri.ReturnRequestId == returnRequest.Id);

            Assert.Equal(ReturnReasonCategory.LegacyUnclassified, item.Category);
            Assert.NotEqual(ReturnReasonCategory.ChangeOfMind, item.Category);
        }

        [Fact]
        public async Task MerchantFault_FullItemReturn_IncludesShippingAndProportionalTax()
        {
            if (SkipIfUnavailable()) return;

            using var setupContext = _fixture.CreateBusinessContext();
            var (orderId, detailAId, _) = await SeedOrderWithTwoLinesAsync(setupContext);
            var returnRequestId = await SeedCompletedReturnAsync(
                setupContext, orderId, detailAId, 1, ReturnReasonCategory.WrongItemOrMerchantFault);

            using var context = _fixture.CreateBusinessContext();
            var service = new RefundOrchestrationService(
                context, new FakeStripeRefundService(), new OrderLifecycleService(context), NullLogger<RefundOrchestrationService>.Instance);

            var result = await service.RequestReturnRefundAsync(returnRequestId, "erreur d'envoi", "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundSucceeded>(result);
            using var verify = _fixture.CreateBusinessContext();
            var refund = await verify.Refunds.AsNoTracking().SingleAsync(r => r.ReturnRequestId == returnRequestId);
            Assert.Equal(60m, refund.MerchandiseAmount);
            Assert.Equal(15m, refund.ShippingAmount);
            Assert.Equal(3m, refund.TaxAmount); // 5 * (60/100)
            Assert.Equal(78m, refund.Amount);
            Assert.Equal(nameof(RefundCause.MerchantFault), refund.Cause);

            // COSMECHIC-BUSINESS-POLICY-001 (section 6) : ORDER_FINANCIAL_SNAPSHOT_IMMUTABLE
            // — le remboursement ne modifie jamais les champs snapshot historiques, seul
            // RefundedAmount (compteur séparé, déjà établi 001B) évolue.
            var order = await verify.OrderHeaders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            Assert.Equal(100m, order.Subtotal);
            Assert.Equal(15m, order.ShippingAmount);
            Assert.Equal(5m, order.TaxAmount);
            Assert.Equal(120m, order.OrderTotal);
            Assert.Equal(78m, order.RefundedAmount);
        }

        [Fact]
        public async Task CustomerRemorse_NeverIncludesShipping()
        {
            if (SkipIfUnavailable()) return;

            using var setupContext = _fixture.CreateBusinessContext();
            var (orderId, detailAId, _) = await SeedOrderWithTwoLinesAsync(setupContext);
            var returnRequestId = await SeedCompletedReturnAsync(
                setupContext, orderId, detailAId, 1, ReturnReasonCategory.ChangeOfMind);

            using var context = _fixture.CreateBusinessContext();
            var service = new RefundOrchestrationService(
                context, new FakeStripeRefundService(), new OrderLifecycleService(context), NullLogger<RefundOrchestrationService>.Instance);

            var result = await service.RequestReturnRefundAsync(returnRequestId, "changement d'avis", "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundSucceeded>(result);
            using var verify = _fixture.CreateBusinessContext();
            var refund = await verify.Refunds.AsNoTracking().SingleAsync(r => r.ReturnRequestId == returnRequestId);
            Assert.Equal(0m, refund.ShippingAmount);
            Assert.Equal(63m, refund.Amount); // 60 + 3 tax + 0 shipping
        }

        [Fact]
        public async Task SuccessivePartialReturns_ShippingRefundedAtMostOnce_TaxNeverExceedsOriginal()
        {
            if (SkipIfUnavailable()) return;

            using var setupContext = _fixture.CreateBusinessContext();
            var (orderId, detailAId, detailBId) = await SeedOrderWithTwoLinesAsync(setupContext);
            var returnA = await SeedCompletedReturnAsync(
                setupContext, orderId, detailAId, 1, ReturnReasonCategory.WrongItemOrMerchantFault);
            var returnB = await SeedCompletedReturnAsync(
                setupContext, orderId, detailBId, 1, ReturnReasonCategory.WrongItemOrMerchantFault);

            using var contextA = _fixture.CreateBusinessContext();
            var serviceA = new RefundOrchestrationService(
                contextA, new FakeStripeRefundService(), new OrderLifecycleService(contextA), NullLogger<RefundOrchestrationService>.Instance);
            var resultA = await serviceA.RequestReturnRefundAsync(returnA, null, "admin-1", SD.ActorTypeAdmin);
            Assert.IsType<RefundSucceeded>(resultA);

            // Deuxième retour, même commande, cause MerchantFault également : la livraison
            // ne doit PAS être remboursée une seconde fois (déjà réclamée par returnA).
            using var contextB = _fixture.CreateBusinessContext();
            var serviceB = new RefundOrchestrationService(
                contextB, new FakeStripeRefundService(), new OrderLifecycleService(contextB), NullLogger<RefundOrchestrationService>.Instance);
            var resultB = await serviceB.RequestReturnRefundAsync(returnB, null, "admin-1", SD.ActorTypeAdmin);
            Assert.IsType<RefundSucceeded>(resultB);

            using var verify = _fixture.CreateBusinessContext();
            var refundA = await verify.Refunds.AsNoTracking().SingleAsync(r => r.ReturnRequestId == returnA);
            var refundB = await verify.Refunds.AsNoTracking().SingleAsync(r => r.ReturnRequestId == returnB);

            Assert.Equal(15m, refundA.ShippingAmount);
            Assert.Equal(0m, refundB.ShippingAmount); // dernier remboursement : livraison déjà épuisée.
            Assert.Equal(3m, refundA.TaxAmount);
            Assert.Equal(2m, refundB.TaxAmount); // 5 - 3 = 2, exactement le solde restant.
            Assert.Equal(5m, refundA.TaxAmount + refundB.TaxAmount); // jamais > TaxAmount original.

            var order = await verify.OrderHeaders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            Assert.Equal(120m, order.RefundedAmount); // 78 + 42 = 120 = OrderTotal exact.
        }

        [Fact]
        public async Task SameReturnRequest_RefundedTwice_SecondAttemptIsRejected()
        {
            if (SkipIfUnavailable()) return;

            using var setupContext = _fixture.CreateBusinessContext();
            var (orderId, detailAId, _) = await SeedOrderWithTwoLinesAsync(setupContext);
            var returnRequestId = await SeedCompletedReturnAsync(
                setupContext, orderId, detailAId, 1, ReturnReasonCategory.WrongItemOrMerchantFault);

            using var context = _fixture.CreateBusinessContext();
            var service = new RefundOrchestrationService(
                context, new FakeStripeRefundService(), new OrderLifecycleService(context), NullLogger<RefundOrchestrationService>.Instance);

            var first = await service.RequestReturnRefundAsync(returnRequestId, null, "admin-1", SD.ActorTypeAdmin);
            Assert.IsType<RefundSucceeded>(first);

            using var context2 = _fixture.CreateBusinessContext();
            var service2 = new RefundOrchestrationService(
                context2, new FakeStripeRefundService(), new OrderLifecycleService(context2), NullLogger<RefundOrchestrationService>.Instance);
            var second = await service2.RequestReturnRefundAsync(returnRequestId, null, "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundRejected>(second);
        }

        [Fact]
        public async Task NotCompletedReturn_IsRejected()
        {
            if (SkipIfUnavailable()) return;

            using var setupContext = _fixture.CreateBusinessContext();
            var (orderId, detailAId, _) = await SeedOrderWithTwoLinesAsync(setupContext);

            var returnRequest = new ReturnRequest { OrderId = orderId, ApplicationUserId = "irrelevant", Status = SD.ReturnStatusReceived, CreatedAt = DateTime.UtcNow };
            setupContext.ReturnRequests.Add(returnRequest);
            await setupContext.SaveChangesAsync();
            setupContext.ReturnItems.Add(new ReturnItem { ReturnRequestId = returnRequest.Id, OrderDetailId = detailAId, Quantity = 1, Category = ReturnReasonCategory.WrongItemOrMerchantFault });
            await setupContext.SaveChangesAsync();

            using var context = _fixture.CreateBusinessContext();
            var service = new RefundOrchestrationService(
                context, new FakeStripeRefundService(), new OrderLifecycleService(context), NullLogger<RefundOrchestrationService>.Instance);

            var result = await service.RequestReturnRefundAsync(returnRequest.Id, null, "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundRejected>(result);
        }

        [Fact]
        public async Task ConcurrentReturnRefunds_SameOrder_NeverExceedRefundableBalance_ShippingClaimedAtMostOnce()
        {
            if (SkipIfUnavailable()) return;

            using var setupContext = _fixture.CreateBusinessContext();
            var (orderId, detailAId, detailBId) = await SeedOrderWithTwoLinesAsync(setupContext);
            var returnA = await SeedCompletedReturnAsync(
                setupContext, orderId, detailAId, 1, ReturnReasonCategory.WrongItemOrMerchantFault);
            var returnB = await SeedCompletedReturnAsync(
                setupContext, orderId, detailBId, 1, ReturnReasonCategory.WrongItemOrMerchantFault);

            using var contextA = _fixture.CreateBusinessContext();
            using var contextB = _fixture.CreateBusinessContext();
            var serviceA = new RefundOrchestrationService(
                contextA, new FakeStripeRefundService(), new OrderLifecycleService(contextA), NullLogger<RefundOrchestrationService>.Instance);
            var serviceB = new RefundOrchestrationService(
                contextB, new FakeStripeRefundService(), new OrderLifecycleService(contextB), NullLogger<RefundOrchestrationService>.Instance);

            // Les deux causes sont MerchantFault : si le calcul n'était pas relu fraîchement
            // à chaque tentative, les deux pourraient réclamer la livraison (15$ chacune) et
            // dépasser ensemble l'OrderTotal (120$). La boucle de concurrence doit
            // l'empêcher.
            var taskA = serviceA.RequestReturnRefundAsync(returnA, "a", "admin-a", SD.ActorTypeAdmin);
            var taskB = serviceB.RequestReturnRefundAsync(returnB, "b", "admin-b", SD.ActorTypeAdmin);
            var results = await Task.WhenAll(taskA, taskB);

            Assert.All(results, r => Assert.IsType<RefundSucceeded>(r));

            using var verify = _fixture.CreateBusinessContext();
            var order = await verify.OrderHeaders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            Assert.True(order.RefundedAmount <= 120m, $"RefundedAmount ({order.RefundedAmount}) a dépassé OrderTotal (120).");
            Assert.Equal(120m, order.RefundedAmount); // 78 + 42, jamais 78 + 57.

            var shippingClaims = await verify.Refunds.AsNoTracking()
                .Where(r => r.OrderId == orderId && r.ShippingAmount > 0)
                .CountAsync();
            Assert.Equal(1, shippingClaims); // livraison réclamée exactement une fois.
        }
    }
}
