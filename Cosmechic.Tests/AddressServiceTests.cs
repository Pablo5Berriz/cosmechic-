using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-ACCOUNT-001 (section 44) : CRUD adresses, ownership, invariant "au plus
    // une adresse par défaut", validation, snapshot historique.
    public class AddressServiceTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly AddressService _sut;

        public AddressServiceTests()
        {
            _sut = new AddressService(_context);
        }

        private static AddressInput ValidInput(string label = "Maison") => new(
            label, "Jean Tremblay", "5145551234", "123 rue Test", "Montréal", "QC", "H2X1Y6", "CA");

        [Fact]
        public async Task CreateAsync_FirstAddress_BecomesDefaultAutomatically()
        {
            var result = await _sut.CreateAsync("user-a", ValidInput(), setAsDefault: false);

            var succeeded = Assert.IsType<AddressSucceeded>(result);
            var stored = await _context.CustomerAddresses.FindAsync(succeeded.AddressId);
            Assert.True(stored!.IsDefaultShipping);
        }

        [Fact]
        public async Task CreateAsync_SecondAddress_NotDefaultUnlessRequested()
        {
            await _sut.CreateAsync("user-a", ValidInput("Maison"), setAsDefault: false);
            var second = await _sut.CreateAsync("user-a", ValidInput("Travail"), setAsDefault: false);

            var succeeded = Assert.IsType<AddressSucceeded>(second);
            var stored = await _context.CustomerAddresses.FindAsync(succeeded.AddressId);
            Assert.False(stored!.IsDefaultShipping);
        }

        [Fact]
        public async Task CreateAsync_SetAsDefault_UnsetsPreviousDefault()
        {
            var first = (AddressSucceeded)await _sut.CreateAsync("user-a", ValidInput("Maison"), setAsDefault: false);
            await _sut.CreateAsync("user-a", ValidInput("Travail"), setAsDefault: true);

            var firstAddress = await _context.CustomerAddresses.FindAsync(first.AddressId);
            var defaults = _context.CustomerAddresses.Count(a => a.ApplicationUserId == "user-a" && a.IsDefaultShipping);

            Assert.False(firstAddress!.IsDefaultShipping);
            Assert.Equal(1, defaults);
        }

        [Fact]
        public async Task CreateAsync_MissingRequiredField_IsRejected()
        {
            var input = ValidInput() with { StreetAddress = "" };

            var result = await _sut.CreateAsync("user-a", input, setAsDefault: false);

            Assert.IsType<AddressRejected>(result);
        }

        [Fact]
        public async Task CreateAsync_NonCanadianCountry_IsRejected()
        {
            var input = ValidInput() with { CountryCode = "US" };

            var result = await _sut.CreateAsync("user-a", input, setAsDefault: false);

            Assert.IsType<AddressRejected>(result);
        }

        [Fact]
        public async Task ListForUserAsync_MultipleAddresses_OnlyReturnsOwnedAddresses()
        {
            await _sut.CreateAsync("user-a", ValidInput("Maison"), setAsDefault: false);
            await _sut.CreateAsync("user-a", ValidInput("Travail"), setAsDefault: false);
            await _sut.CreateAsync("user-b", ValidInput("Chalet"), setAsDefault: false);

            var addressesA = await _sut.ListForUserAsync("user-a");

            Assert.Equal(2, addressesA.Count);
            Assert.All(addressesA, a => Assert.Equal("user-a", a.ApplicationUserId));
        }

        [Fact]
        public async Task GetOwnedAsync_ForeignAddress_ReturnsNull()
        {
            var created = (AddressSucceeded)await _sut.CreateAsync("user-a", ValidInput(), setAsDefault: false);

            var owned = await _sut.GetOwnedAsync(created.AddressId, "user-b");

            Assert.Null(owned);
        }

        [Fact]
        public async Task UpdateAsync_ForeignAddress_IsRejected()
        {
            var created = (AddressSucceeded)await _sut.CreateAsync("user-a", ValidInput(), setAsDefault: false);

            var result = await _sut.UpdateAsync(created.AddressId, "user-b", ValidInput("Nouvelle etiquette"));

            Assert.IsType<AddressRejected>(result);
        }

        [Fact]
        public async Task UpdateAsync_OwnedAddress_PersistsChanges()
        {
            var created = (AddressSucceeded)await _sut.CreateAsync("user-a", ValidInput("Maison"), setAsDefault: false);

            await _sut.UpdateAsync(created.AddressId, "user-a", ValidInput("Chalet"));

            var stored = await _context.CustomerAddresses.FindAsync(created.AddressId);
            Assert.Equal("Chalet", stored!.Label);
        }

        [Fact]
        public async Task DeleteAsync_ForeignAddress_IsRejected()
        {
            var created = (AddressSucceeded)await _sut.CreateAsync("user-a", ValidInput(), setAsDefault: false);

            var result = await _sut.DeleteAsync(created.AddressId, "user-b");

            Assert.IsType<AddressRejected>(result);
            Assert.NotNull(await _context.CustomerAddresses.FindAsync(created.AddressId));
        }

        [Fact]
        public async Task DeleteAsync_DefaultAddress_PromotesAnotherToDefault()
        {
            var first = (AddressSucceeded)await _sut.CreateAsync("user-a", ValidInput("Maison"), setAsDefault: false);
            var second = (AddressSucceeded)await _sut.CreateAsync("user-a", ValidInput("Travail"), setAsDefault: false);

            await _sut.DeleteAsync(first.AddressId, "user-a");

            var remaining = await _context.CustomerAddresses.FindAsync(second.AddressId);
            Assert.True(remaining!.IsDefaultShipping);
        }

        [Fact]
        public async Task SetDefaultAsync_ForeignAddress_IsRejected()
        {
            var created = (AddressSucceeded)await _sut.CreateAsync("user-a", ValidInput(), setAsDefault: false);

            var result = await _sut.SetDefaultAsync(created.AddressId, "user-b");

            Assert.IsType<AddressRejected>(result);
        }

        [Fact]
        public async Task SetDefaultAsync_NeverLeavesTwoDefaultsForSameUser()
        {
            var first = (AddressSucceeded)await _sut.CreateAsync("user-a", ValidInput("Maison"), setAsDefault: false);
            var second = (AddressSucceeded)await _sut.CreateAsync("user-a", ValidInput("Travail"), setAsDefault: false);

            await _sut.SetDefaultAsync(second.AddressId, "user-a");

            var defaults = _context.CustomerAddresses.Count(a => a.ApplicationUserId == "user-a" && a.IsDefaultShipping);
            Assert.Equal(1, defaults);
            var firstAddress = await _context.CustomerAddresses.FindAsync(first.AddressId);
            Assert.False(firstAddress!.IsDefaultShipping);
        }

        public void Dispose() => _context.Dispose();
    }
}
