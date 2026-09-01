using System.Net;
using Cosmechic.Models;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-ACCOUNT-001 (section 15/28/42) : intégration adresse enregistrée <->
    // checkout — ownership de l'adresse sélectionnée, et immuabilité du snapshot
    // historique lorsque l'adresse enregistrée est modifiée après coup.
    public class CheckoutSavedAddressTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public CheckoutSavedAddressTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        private int SeedShippingMethod()
        {
            var id = 0;
            _factory.Seed(context =>
            {
                var method = new ShippingMethod { Name = "Standard", Price = 10.00m, IsActive = true, SortOrder = 1 };
                context.ShippingMethods.Add(method);
                context.SaveChanges();
                id = method.ShippingMethodId;
            });
            return id;
        }

        private int SeedAddress(string userId, string street = "1 rue Originale")
        {
            var id = 0;
            _factory.Seed(context =>
            {
                var address = new CustomerAddress
                {
                    ApplicationUserId = userId,
                    Label = "Maison",
                    RecipientName = "Alice Test",
                    PhoneNumber = "5145551234",
                    StreetAddress = street,
                    City = "Montreal",
                    State = "QC",
                    PostalCode = "H0H0H0",
                    CountryCode = "CA",
                    IsDefaultShipping = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                context.CustomerAddresses.Add(address);
                context.SaveChanges();
                id = address.Id;
            });
            return id;
        }

        [Fact]
        public async Task SummaryPOST_SelectedAddressId_UsesSavedAddressSnapshot()
        {
            var shippingMethodId = SeedShippingMethod();
            var addressId = SeedAddress(TestIdentities.CustomerAId, street: "1 rue Originale");
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync(
                "/Cart/Summary",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["SelectedAddressId"] = addressId.ToString(),
                    ["ShippingMethodId"] = shippingMethodId.ToString(),
                }));

            Assert.Equal((HttpStatusCode)303, response.StatusCode);
            var order = _factory.Query(ctx => ctx.OrderHeaders
                .OrderByDescending(o => o.Id)
                .First(o => o.ApplicationUserId == TestIdentities.CustomerAId && o.SessionId == _factory.StripeCheckoutService.SessionIdToReturn));

            Assert.Equal("1 rue Originale", order.StreetAddress);
            Assert.Equal("Alice Test", order.Name);
        }

        [Fact]
        public async Task SummaryPOST_SelectedAddressId_ForeignAddress_IsRejected_NoOrderCreated()
        {
            var shippingMethodId = SeedShippingMethod();
            var addressId = SeedAddress(TestIdentities.CustomerBId);
            var client = _factory.CreateTestClient().AsCustomerA();

            var beforeCount = _factory.Query(ctx => ctx.OrderHeaders.Count());

            var response = await client.PostAsync(
                "/Cart/Summary",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["SelectedAddressId"] = addressId.ToString(),
                    ["ShippingMethodId"] = shippingMethodId.ToString(),
                }));

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            var afterCount = _factory.Query(ctx => ctx.OrderHeaders.Count());
            Assert.Equal(beforeCount, afterCount);
        }

        [Fact]
        public async Task SavedAddressChangedAfterOrder_HistoricalOrderAddressUnchanged()
        {
            var shippingMethodId = SeedShippingMethod();
            var addressId = SeedAddress(TestIdentities.CustomerAId, street: "1 rue Originale");
            var client = _factory.CreateTestClient().AsCustomerA();

            await client.PostAsync(
                "/Cart/Summary",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["SelectedAddressId"] = addressId.ToString(),
                    ["ShippingMethodId"] = shippingMethodId.ToString(),
                }));

            var order = _factory.Query(ctx => ctx.OrderHeaders
                .OrderByDescending(o => o.Id)
                .First(o => o.ApplicationUserId == TestIdentities.CustomerAId && o.SessionId == _factory.StripeCheckoutService.SessionIdToReturn));

            // Le client déménage : l'adresse enregistrée change après la commande.
            await client.PostAsync(
                $"/Account/EditAddress/{addressId}",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Label"] = "Maison",
                    ["RecipientName"] = "Alice Test",
                    ["PhoneNumber"] = "5145551234",
                    ["StreetAddress"] = "999 nouvelle adresse",
                    ["City"] = "Quebec",
                    ["State"] = "QC",
                    ["PostalCode"] = "G1G1G1",
                    ["CountryCode"] = "CA",
                }));

            var updatedAddress = _factory.Query(ctx => ctx.CustomerAddresses.First(a => a.Id == addressId));
            var reloadedOrder = _factory.Query(ctx => ctx.OrderHeaders.First(o => o.Id == order.Id));

            Assert.Equal("999 nouvelle adresse", updatedAddress.StreetAddress);
            Assert.Equal("1 rue Originale", reloadedOrder.StreetAddress);
        }

        public void Dispose() => _factory.Dispose();
    }
}
