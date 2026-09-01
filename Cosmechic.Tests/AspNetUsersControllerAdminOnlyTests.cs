using System.Net;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-ACCOUNT-001 (section 7/26/29) : AspNetUsersController redevient un CRUD
    // scaffold strictement administratif (le client passe désormais par
    // AccountController.Profile) — recertifie l'autorisation et recertifie que même un
    // Admin ne peut plus altérer les champs Identity sensibles via ce formulaire (l'ancien
    // _context.Update(aspNetUser) sur un [Bind] narrowé les écrasait silencieusement,
    // même motif que le correctif OrderHeadersController.Edit,
    // COSMECHIC-COMMERCE-OPERATIONS-001B-CLOSURE-1).
    public class AspNetUsersControllerAdminOnlyTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public AspNetUsersControllerAdminOnlyTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        [Fact]
        public async Task Index_Customer_IsDenied()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync("/AspNetUsers/Index");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Details_Customer_IsDenied_EvenOnOwnProfile()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/AspNetUsers/Details/{TestIdentities.CustomerAId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Edit_Customer_IsDenied_EvenOnOwnProfile()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var getResponse = await client.GetAsync($"/AspNetUsers/Edit/{TestIdentities.CustomerAId}");
            Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

            var postResponse = await client.PostAsync(
                $"/AspNetUsers/Edit/{TestIdentities.CustomerAId}",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Id"] = TestIdentities.CustomerAId,
                    ["PhoneNumber"] = "5145550000",
                }));
            Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
        }

        [Fact]
        public async Task Edit_Admin_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.GetAsync($"/AspNetUsers/Edit/{TestIdentities.CustomerAId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Edit_AdminPost_CannotTamperWithIdentitySensitiveFields()
        {
            var before = _factory.Query(ctx => ctx.AspNetUsers.Single(u => u.Id == TestIdentities.CustomerAId));
            var originalPasswordHash = before.PasswordHash;
            var originalSecurityStamp = before.SecurityStamp;
            var originalNormalizedUserName = before.NormalizedUserName;
            var originalEmailConfirmed = before.EmailConfirmed;

            var client = _factory.CreateTestClient().AsAdmin();

            var response = await client.PostAsync(
                $"/AspNetUsers/Edit/{TestIdentities.CustomerAId}",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Id"] = TestIdentities.CustomerAId,
                    ["PhoneNumber"] = "5145550000",
                    ["StreetAddress"] = "999 rue Modifiee",
                    ["City"] = "Laval",
                    ["State"] = "QC",
                    ["PostalCode"] = "H1H1H1",
                    // Tentative d'overposting : hors du DTO AspNetUserEditInput, ignorée.
                    ["PasswordHash"] = "tampered",
                    ["SecurityStamp"] = "tampered",
                    ["EmailConfirmed"] = "true",
                    ["UserName"] = "tampered-username",
                    ["Email"] = "tampered@example.test",
                }));

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);

            var after = _factory.Query(ctx => ctx.AspNetUsers.Single(u => u.Id == TestIdentities.CustomerAId));
            Assert.Equal(originalPasswordHash, after.PasswordHash);
            Assert.Equal(originalSecurityStamp, after.SecurityStamp);
            Assert.Equal(originalNormalizedUserName, after.NormalizedUserName);
            Assert.Equal(originalEmailConfirmed, after.EmailConfirmed);
            Assert.Equal("customer-a", after.UserName);

            // Champs légitimement adressés par ce formulaire : bien appliqués.
            Assert.Equal("999 rue Modifiee", after.StreetAddress);
        }

        public void Dispose() => _factory.Dispose();
    }
}
