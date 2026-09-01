using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-BUSINESS-POLICY-001 (section 8) : l'export de données personnelles est
    // maintenant étendu (Addresses/Orders/ReturnRequests/Refunds) — chaque requête filtre
    // explicitement sur l'utilisateur authentifié, jamais un identifiant fourni par le
    // client. IDOR testé explicitement : l'export de B ne contient jamais rien de A, et
    // vice versa.
    public class PersonalDataExportTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public PersonalDataExportTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        [Fact]
        public async Task Export_OwnData_ContainsOwnOrder_NotOtherCustomersOrder()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync("/Identity/Account/Manage/DownloadPersonalData", new FormUrlEncodedContent(new Dictionary<string, string>()));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();

            // Commande de A (TestDataSeeder.OrderHeaderAId) présente...
            Assert.Contains("\"OrderId\": 1", body);
            // ...commande de B totalement absente (IDOR).
            Assert.DoesNotContain("\"OrderId\": 2", body);
            Assert.DoesNotContain("customer-b", body);
        }

        [Fact]
        public async Task Export_DifferentCustomer_NeverContainsFirstCustomersData()
        {
            var clientB = _factory.CreateTestClient().AsCustomerB();

            var response = await clientB.PostAsync("/Identity/Account/Manage/DownloadPersonalData", new FormUrlEncodedContent(new Dictionary<string, string>()));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("\"OrderId\": 2", body);
            Assert.DoesNotContain("\"OrderId\": 1", body);
            Assert.DoesNotContain("customer-a", body);
        }

        [Fact]
        public async Task Export_IsWellFormedJson_WithExpectedTopLevelSections()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync("/Identity/Account/Manage/DownloadPersonalData", new FormUrlEncodedContent(new Dictionary<string, string>()));
            var body = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body); // ne lève pas si le JSON est mal formé.
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("Profile", out _));
            Assert.True(root.TryGetProperty("Addresses", out _));
            Assert.True(root.TryGetProperty("Orders", out _));
            Assert.True(root.TryGetProperty("ReturnRequests", out _));
            Assert.True(root.TryGetProperty("Refunds", out _));
        }

        [Fact]
        public async Task Export_NeverContainsInternalOrSecretFields()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync("/Identity/Account/Manage/DownloadPersonalData", new FormUrlEncodedContent(new Dictionary<string, string>()));
            var body = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain("StripeRefundId", body);
            Assert.DoesNotContain("IdempotencyKey", body);
            Assert.DoesNotContain("FailureCode", body);
            Assert.DoesNotContain("AdminComment", body);
            Assert.DoesNotContain("PaymentIntentId", body);
            Assert.DoesNotContain("PasswordHash", body);
            Assert.DoesNotContain("RowVersion", body);
        }

        [Fact]
        public async Task Export_Anonymous_RequiresAuthentication()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.PostAsync("/Identity/Account/Manage/DownloadPersonalData", new FormUrlEncodedContent(new Dictionary<string, string>()));

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }

        public void Dispose() => _factory.Dispose();
    }
}
