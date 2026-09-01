using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-LEGAL-READINESS-001 (section 4) : le reçu n'est jamais transformé en
    // facture fiscale légale sans données réelles — aucune conformité fiscale revendiquée,
    // aucune donnée vendeur fictive, aucun numéro d'enregistrement TPS/TVQ inventé, montants
    // toujours issus du snapshot financier persisté.
    public class ReceiptInvoiceAuditTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public ReceiptInvoiceAuditTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        [Fact]
        public async Task Receipt_NeverClaimsOfficialTaxInvoiceCompliance()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var html = await (await client.GetAsync($"/Cart/Receipt/{TestDataSeeder.OrderHeaderAId}")).Content.ReadAsStringAsync();

            Assert.Contains("Ce n'est pas une facture fiscale officielle", html);
        }

        [Fact]
        public async Task Receipt_NeverContainsFabricatedSellerOrTaxRegistrationData()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var html = await (await client.GetAsync($"/Cart/Receipt/{TestDataSeeder.OrderHeaderAId}")).Content.ReadAsStringAsync();

            // Aucune valeur inventée pour LegalBusinessName/BusinessAddress/
            // TaxRegistrationNumbers : la config par défaut (BusinessInformationOptions) les
            // laisse vides — s'ils apparaissaient ici avec une vraie valeur, ce serait soit
            // une config réelle (acceptable), soit une fabrication (jamais acceptable). On
            // vérifie ici le comportement par défaut réel de ce dépôt : rien de fiscal
            // n'est affirmé.
            Assert.DoesNotContain("NEQ", html);
            Assert.DoesNotContain("TPS/TVQ", html);
            Assert.DoesNotContain("numéro d'entreprise", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Receipt_NeverExposesInternalStripeOrAdminData()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var html = await (await client.GetAsync($"/Cart/Receipt/{TestDataSeeder.OrderHeaderAId}")).Content.ReadAsStringAsync();

            Assert.DoesNotContain("PaymentIntentId", html);
            Assert.DoesNotContain("StripeRefundId", html);
            Assert.DoesNotContain("IdempotencyKey", html);
            Assert.DoesNotContain("FailureCode", html);
            Assert.DoesNotContain("AdminComment", html);
        }

        [Fact]
        public async Task Receipt_TotalsMatchPersistedFinancialSnapshot_ExactAmounts()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var html = await (await client.GetAsync($"/Cart/Receipt/{TestDataSeeder.OrderHeaderAId}")).Content.ReadAsStringAsync();

            // TestDataSeeder : OrderHeaderA a Subtotal=25.00, OrderTotal=25.00.
            Assert.Contains("25.00 CAD", html);
        }

        [Fact]
        public async Task Receipt_OtherCustomersOrder_IsForbidden()
        {
            var client = _factory.CreateTestClient().AsCustomerB();

            var response = await client.GetAsync($"/Cart/Receipt/{TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        public void Dispose() => _factory.Dispose();
    }
}
