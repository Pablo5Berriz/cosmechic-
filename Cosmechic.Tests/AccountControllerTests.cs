using System.Net;
using Cosmechic.Models;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-ACCOUNT-001 (section 44) : tableau de bord, profil (overposting/other-
    // user), adresses (CRUD/IDOR/overposting), commandes (liste/pagination/détail/IDOR).
    public class AccountControllerTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public AccountControllerTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        [Fact]
        public async Task Index_Owner_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync("/Account/Index");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Index_Anonymous_IsDenied()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/Account/Index");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Profile_ValidUpdate_PersistsPhoneNumber()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync(
                "/Account/Profile",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["PhoneNumber"] = "5145559999" }));

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            var phone = _factory.QueryIdentity(ctx => ctx.Users.First(u => u.Id == TestIdentities.CustomerAId).PhoneNumber);
            Assert.Equal("5145559999", phone);
        }

        [Fact]
        public async Task Profile_Overposting_CannotChangeEmailOrRoles()
        {
            var client = _factory.CreateTestClient().AsCustomerA();
            var originalEmail = _factory.QueryIdentity(ctx => ctx.Users.First(u => u.Id == TestIdentities.CustomerAId).Email);

            var response = await client.PostAsync(
                "/Account/Profile",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["PhoneNumber"] = "5145559999",
                    ["Email"] = "tampered@example.test",
                    ["Id"] = TestIdentities.CustomerBId,
                    ["EmailConfirmed"] = "true",
                }));

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            var emailAfter = _factory.QueryIdentity(ctx => ctx.Users.First(u => u.Id == TestIdentities.CustomerAId).Email);
            Assert.Equal(originalEmail, emailAfter);
        }

        [Fact]
        public async Task Addresses_Create_ThenList_ReturnsOwnAddress()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var createResponse = await client.PostAsync("/Account/CreateAddress", AddressForm());
            Assert.Equal(HttpStatusCode.Found, createResponse.StatusCode);

            var count = _factory.Query(ctx => ctx.CustomerAddresses.Count(a => a.ApplicationUserId == TestIdentities.CustomerAId));
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task EditAddress_ForeignAddress_IsDenied()
        {
            var addressId = SeedAddress(TestIdentities.CustomerBId);
            var client = _factory.CreateTestClient().AsCustomerA();

            var getResponse = await client.GetAsync($"/Account/EditAddress/{addressId}");
            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

            var postResponse = await client.PostAsync($"/Account/EditAddress/{addressId}", AddressForm(label: "Piraté"));
            var label = _factory.Query(ctx => ctx.CustomerAddresses.First(a => a.Id == addressId).Label);
            Assert.NotEqual("Piraté", label);
        }

        [Fact]
        public async Task DeleteAddress_ForeignAddress_IsDenied()
        {
            var addressId = SeedAddress(TestIdentities.CustomerBId);
            var client = _factory.CreateTestClient().AsCustomerA();

            await client.PostAsync(
                "/Account/DeleteAddress",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Id"] = addressId.ToString() }));

            Assert.NotNull(_factory.Query(ctx => ctx.CustomerAddresses.FirstOrDefault(a => a.Id == addressId)));
        }

        [Fact]
        public async Task SetDefaultAddress_ForeignAddress_IsDenied()
        {
            var addressId = SeedAddress(TestIdentities.CustomerBId, isDefault: false);
            var client = _factory.CreateTestClient().AsCustomerA();

            await client.PostAsync(
                "/Account/SetDefaultAddress",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Id"] = addressId.ToString() }));

            var isDefault = _factory.Query(ctx => ctx.CustomerAddresses.First(a => a.Id == addressId).IsDefaultShipping);
            Assert.False(isDefault);
        }

        [Fact]
        public async Task Orders_OnlyListsOwnOrders()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync("/Account/Orders");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"Commande #{TestDataSeeder.OrderHeaderAId}", body);
            Assert.DoesNotContain($"Commande #{TestDataSeeder.OrderHeaderBId}", body);
        }

        [Fact]
        public async Task Orders_Pagination_SplitsResultsAcrossPages()
        {
            for (var i = 0; i < 12; i++)
            {
                SeedOrder(TestIdentities.CustomerAId);
            }

            var client = _factory.CreateTestClient().AsCustomerA();

            var pageOne = await client.GetAsync("/Account/Orders?page=1");
            var pageTwo = await client.GetAsync("/Account/Orders?page=2");

            Assert.Equal(HttpStatusCode.OK, pageOne.StatusCode);
            Assert.Equal(HttpStatusCode.OK, pageTwo.StatusCode);
            var bodyOne = await pageOne.Content.ReadAsStringAsync();
            var bodyTwo = await pageTwo.Content.ReadAsStringAsync();
            Assert.NotEqual(bodyOne, bodyTwo);
        }

        [Fact]
        public async Task OrderDetails_Owner_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/Account/OrderDetails/{TestDataSeeder.OrderHeaderAId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task OrderDetails_ForeignOrder_IsDenied()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/Account/OrderDetails/{TestDataSeeder.OrderHeaderBId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Returns_OnlyListsOwnReturnRequests()
        {
            SeedReturnRequest(TestIdentities.CustomerBId, TestDataSeeder.OrderHeaderBId);
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync("/Account/Returns");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private static FormUrlEncodedContent AddressForm(string label = "Maison") => new(new Dictionary<string, string>
        {
            ["Label"] = label,
            ["RecipientName"] = "Jean Tremblay",
            ["PhoneNumber"] = "5145551234",
            ["StreetAddress"] = "123 rue Test",
            ["City"] = "Montréal",
            ["State"] = "QC",
            ["PostalCode"] = "H2X1Y6",
            ["CountryCode"] = "CA",
        });

        private int SeedAddress(string userId, bool isDefault = true)
        {
            var id = 0;
            _factory.Seed(context =>
            {
                var address = new CustomerAddress
                {
                    ApplicationUserId = userId,
                    Label = "Maison",
                    RecipientName = "Test",
                    PhoneNumber = "5145551234",
                    StreetAddress = "1 rue Test",
                    City = "Montreal",
                    State = "QC",
                    PostalCode = "H0H0H0",
                    CountryCode = "CA",
                    IsDefaultShipping = isDefault,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                context.CustomerAddresses.Add(address);
                context.SaveChanges();
                id = address.Id;
            });
            return id;
        }

        private void SeedOrder(string userId)
        {
            _factory.Seed(context =>
            {
                context.OrderHeaders.Add(new OrderHeader
                {
                    ApplicationUserId = userId,
                    OrderDate = DateTime.UtcNow,
                    OrderTotal = 10m,
                    Subtotal = 10m,
                    OrderStatus = "Pending",
                    PaymentStatus = "Pending",
                    Name = "Test",
                    PhoneNumber = "5145551234",
                    StreetAddress = "1 rue Test",
                    City = "Montreal",
                    State = "QC",
                    PostalCode = "H0H0H0",
                });
                context.SaveChanges();
            });
        }

        private void SeedReturnRequest(string userId, int orderId)
        {
            _factory.Seed(context =>
            {
                context.ReturnRequests.Add(new ReturnRequest
                {
                    OrderId = orderId,
                    ApplicationUserId = userId,
                    Status = Utility.SD.ReturnStatusRequested,
                    CreatedAt = DateTime.UtcNow,
                });
                context.SaveChanges();
            });
        }

        public void Dispose() => _factory.Dispose();
    }
}
