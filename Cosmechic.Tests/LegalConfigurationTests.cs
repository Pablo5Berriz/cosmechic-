using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-LEGAL-FINALIZATION-001 (section 6) : LEGAL_CONFIGURATION_COMPLETE rendue
    // calculable/testable, sans jamais fabriquer une valeur — LegalConfigurationEvaluator est
    // une classe pure, testée directement (même patron que WcagContrast).
    public class LegalConfigurationEvaluatorTests
    {
        [Fact]
        public void EvaluateSellerIdentity_AllFieldsEmpty_IsIncomplete()
        {
            var options = new BusinessInformationOptions();

            Assert.Equal(LegalConfigurationState.Incomplete, LegalConfigurationEvaluator.EvaluateSellerIdentity(options));
        }

        [Fact]
        public void EvaluateSellerIdentity_NameOnlyNoAddress_IsIncomplete()
        {
            var options = new BusinessInformationOptions { LegalBusinessName = "Cosmechic inc." };

            Assert.Equal(LegalConfigurationState.Incomplete, LegalConfigurationEvaluator.EvaluateSellerIdentity(options));
        }

        [Fact]
        public void EvaluateSellerIdentity_NameAndFullAddress_IsComplete()
        {
            var options = new BusinessInformationOptions
            {
                LegalBusinessName = "Cosmechic inc.",
                BusinessStreetAddress = "1 rue Réelle",
                BusinessCity = "Montréal",
                BusinessProvince = "QC",
                BusinessPostalCode = "H0H 0H0",
                BusinessCountry = "CA",
            };

            Assert.Equal(LegalConfigurationState.Complete, LegalConfigurationEvaluator.EvaluateSellerIdentity(options));
        }

        [Fact]
        public void EvaluateSellerIdentity_PartialAddress_IsIncomplete()
        {
            var options = new BusinessInformationOptions
            {
                LegalBusinessName = "Cosmechic inc.",
                BusinessStreetAddress = "1 rue Réelle",
                BusinessCity = "Montréal",
                // Province/PostalCode/Country manquants.
            };

            Assert.Equal(LegalConfigurationState.Incomplete, LegalConfigurationEvaluator.EvaluateSellerIdentity(options));
        }

        [Fact]
        public void EvaluateGstRegistration_Unknown_IsIncomplete()
        {
            var options = new BusinessInformationOptions { GstRegistrationStatus = TaxRegistrationStatus.Unknown };

            Assert.Equal(LegalConfigurationState.Incomplete, LegalConfigurationEvaluator.EvaluateGstRegistration(options));
        }

        [Fact]
        public void EvaluateGstRegistration_NotRegistered_IsNotApplicable()
        {
            var options = new BusinessInformationOptions { GstRegistrationStatus = TaxRegistrationStatus.NotRegistered };

            Assert.Equal(LegalConfigurationState.NotApplicable, LegalConfigurationEvaluator.EvaluateGstRegistration(options));
        }

        [Fact]
        public void EvaluateGstRegistration_RegisteredWithoutNumber_IsIncomplete()
        {
            var options = new BusinessInformationOptions { GstRegistrationStatus = TaxRegistrationStatus.Registered, GstNumber = null };

            Assert.Equal(LegalConfigurationState.Incomplete, LegalConfigurationEvaluator.EvaluateGstRegistration(options));
        }

        [Fact]
        public void EvaluateGstRegistration_RegisteredWithNumber_IsComplete()
        {
            var options = new BusinessInformationOptions { GstRegistrationStatus = TaxRegistrationStatus.Registered, GstNumber = "123456789RT0001" };

            Assert.Equal(LegalConfigurationState.Complete, LegalConfigurationEvaluator.EvaluateGstRegistration(options));
        }

        [Fact]
        public void EvaluateQstRegistration_MirrorsGstLogic_RegisteredWithNumber_IsComplete()
        {
            var options = new BusinessInformationOptions { QstRegistrationStatus = TaxRegistrationStatus.Registered, QstNumber = "1234567890TQ0001" };

            Assert.Equal(LegalConfigurationState.Complete, LegalConfigurationEvaluator.EvaluateQstRegistration(options));
        }

        [Fact]
        public void EvaluateOverall_DefaultOptions_IsIncomplete()
        {
            // Configuration par défaut réelle de ce dépôt (appsettings.json) : nom/adresse
            // vides, statuts de taxe Unknown — jamais "Complete" par défaut.
            var options = new BusinessInformationOptions();

            Assert.Equal(LegalConfigurationState.Incomplete, LegalConfigurationEvaluator.EvaluateOverall(options));
        }

        // Identité vendeur configurée + les deux taxes explicitement tranchées à
        // NotRegistered : tout ce qui doit être vrai l'est — Complete, pas NotApplicable
        // (NotApplicable global est réservé au cas où TOUT est NotApplicable, ce qui n'arrive
        // jamais ici puisque EvaluateSellerIdentity n'est jamais NotApplicable).
        [Fact]
        public void EvaluateOverall_IdentityCompleteBothTaxesNotRegistered_IsComplete()
        {
            var options = new BusinessInformationOptions
            {
                LegalBusinessName = "Cosmechic inc.",
                BusinessStreetAddress = "1 rue Réelle",
                BusinessCity = "Montréal",
                BusinessProvince = "QC",
                BusinessPostalCode = "H0H 0H0",
                BusinessCountry = "CA",
                GstRegistrationStatus = TaxRegistrationStatus.NotRegistered,
                QstRegistrationStatus = TaxRegistrationStatus.NotRegistered,
            };

            Assert.Equal(LegalConfigurationState.Complete, LegalConfigurationEvaluator.EvaluateOverall(options));
        }

        [Fact]
        public void EvaluateOverall_IdentityCompleteOneTaxRegisteredWithNumber_OtherNotRegistered_IsComplete()
        {
            var options = new BusinessInformationOptions
            {
                LegalBusinessName = "Cosmechic inc.",
                BusinessStreetAddress = "1 rue Réelle",
                BusinessCity = "Montréal",
                BusinessProvince = "QC",
                BusinessPostalCode = "H0H 0H0",
                BusinessCountry = "CA",
                GstRegistrationStatus = TaxRegistrationStatus.Registered,
                GstNumber = "123456789RT0001",
                QstRegistrationStatus = TaxRegistrationStatus.NotRegistered,
            };

            Assert.Equal(LegalConfigurationState.Complete, LegalConfigurationEvaluator.EvaluateOverall(options));
        }

        [Fact]
        public void EvaluateOverall_OneTaxStatusStillUnknown_IsIncomplete()
        {
            var options = new BusinessInformationOptions
            {
                LegalBusinessName = "Cosmechic inc.",
                BusinessStreetAddress = "1 rue Réelle",
                BusinessCity = "Montréal",
                BusinessProvince = "QC",
                BusinessPostalCode = "H0H 0H0",
                BusinessCountry = "CA",
                GstRegistrationStatus = TaxRegistrationStatus.NotRegistered,
                QstRegistrationStatus = TaxRegistrationStatus.Unknown,
            };

            Assert.Equal(LegalConfigurationState.Incomplete, LegalConfigurationEvaluator.EvaluateOverall(options));
        }
    }

    // COSMECHIC-LEGAL-FINALIZATION-001 (section 6/16) : avec la configuration par défaut
    // réelle de ce dépôt (aucune valeur légale/fiscale renseignée), aucune page publique ne
    // doit jamais afficher de placeholder juridique/fiscal fictif.
    public class NoLegalPlaceholderTests : IDisposable
    {
        private static readonly string[] ForbiddenPlaceholders =
        {
            "123456789 RT0001",
            "123456789RT0001",
            "1234567890 TQ0001",
            "1234567890TQ0001",
            "000-000-0000",
            "555-555-5555",
            "Lorem ipsum",
            "123 rue Exemple",
            "123 Main St",
        };

        private readonly CustomWebApplicationFactory _factory = new();

        [Theory]
        [InlineData("/Home/About")]
        [InlineData("/Home/Contact")]
        [InlineData("/Home/Privacy")]
        [InlineData("/Home/Terms")]
        [InlineData("/Home/Returns")]
        [InlineData("/Home/Faq")]
        [InlineData("/Home/Shipping")]
        public async Task PublicPage_WithDefaultUnconfiguredBusinessInformation_NeverShowsLegalPlaceholder(string path)
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync();

            foreach (var placeholder in ForbiddenPlaceholders)
            {
                Assert.DoesNotContain(placeholder, html);
            }
        }

        [Fact]
        public async Task Receipt_WithDefaultUnconfiguredBusinessInformation_NeverShowsLegalPlaceholder()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/Cart/Receipt/{TestDataSeeder.OrderHeaderAId}");
            var html = await response.Content.ReadAsStringAsync();

            foreach (var placeholder in ForbiddenPlaceholders)
            {
                Assert.DoesNotContain(placeholder, html);
            }
        }

        public void Dispose() => _factory.Dispose();
    }

    // COSMECHIC-LEGAL-FINALIZATION-001 (section 11/16) : recertification — une demande en
    // NeedsSafetyReview ne peut jamais emprunter le chemin de remboursement ordinaire, même
    // directement au niveau service (pas seulement via l'UI/autorisation).
    public class SafetyReviewCannotBeRefundedTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();

        [Fact]
        public async Task RequestReturnRefundAsync_OnReturnStillInNeedsSafetyReview_IsRejected()
        {
            var lifecycleService = new OrderLifecycleService(_context);
            var refundService = new RefundOrchestrationService(
                _context, new FakeStripeRefundService(), lifecycleService, Microsoft.Extensions.Logging.Abstractions.NullLogger<RefundOrchestrationService>.Instance);

            var order = new OrderHeader
            {
                ApplicationUserId = "user-a",
                OrderDate = DateTime.UtcNow,
                OrderTotal = 25m,
                Subtotal = 25m,
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
            _context.OrderHeaders.Add(order);
            await _context.SaveChangesAsync();

            var returnRequest = new ReturnRequest
            {
                OrderId = order.Id,
                ApplicationUserId = "user-a",
                Status = SD.ReturnStatusNeedsSafetyReview,
                CreatedAt = DateTime.UtcNow,
            };
            _context.ReturnRequests.Add(returnRequest);
            await _context.SaveChangesAsync();

            var result = await refundService.RequestReturnRefundAsync(returnRequest.Id, null, "admin-1", SD.ActorTypeAdmin);

            Assert.IsType<RefundRejected>(result);
        }

        public void Dispose() => _context.Dispose();
    }
}
