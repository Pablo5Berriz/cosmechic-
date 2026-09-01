using Cosmechic.Data;
using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Cosmechic.Tests
{
    // COSMECHIC-ACCOUNT-001 (section 13/41/44) : invariant "au plus une adresse de
    // livraison par défaut" vérifié contre un SQL Server réel (pas InMemory, qui
    // n'applique jamais l'index unique filtré) — y compris sous concurrence réelle.
    [Collection("SqlServerFixture collection")]
    public class SqlServerAddressTests
    {
        private readonly SqlServerFixture _fixture;
        private readonly ITestOutputHelper _output;

        public SqlServerAddressTests(SqlServerFixture fixture, ITestOutputHelper output)
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

        private static CustomerAddress BuildAddress(string userId, string label, bool isDefault) => new()
        {
            ApplicationUserId = userId,
            Label = label,
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

        [Fact]
        public async Task ExistingCustomerWithOrderHistory_CanAddMultipleAddresses()
        {
            if (SkipIfUnavailable()) return;

            var userId = await SeedUserAsync();
            using (var setupContext = _fixture.CreateBusinessContext())
            {
                setupContext.OrderHeaders.Add(new OrderHeader
                {
                    ApplicationUserId = userId,
                    OrderDate = DateTime.UtcNow,
                    OrderTotal = 42m,
                    Subtotal = 42m,
                    OrderStatus = "Pending",
                    PaymentStatus = "Pending",
                    Name = "Test",
                    PhoneNumber = "5145551234",
                    StreetAddress = "1 rue Historique",
                    City = "Montreal",
                    State = "QC",
                    PostalCode = "H0H0H0",
                });
                await setupContext.SaveChangesAsync();
            }

            using var context = _fixture.CreateBusinessContext();
            var addressService = new AddressService(context);
            var first = await addressService.CreateAsync(userId, new AddressInput("Maison", "Test", "5145551234", "1 rue Test", "Montreal", "QC", "H0H0H0", "CA"), setAsDefault: false);
            var second = await addressService.CreateAsync(userId, new AddressInput("Travail", "Test", "5145551234", "2 rue Test", "Quebec", "QC", "G1G1G1", "CA"), setAsDefault: false);

            Assert.IsType<AddressSucceeded>(first);
            Assert.IsType<AddressSucceeded>(second);

            using var verify = _fixture.CreateBusinessContext();
            var addressCount = await verify.CustomerAddresses.AsNoTracking().CountAsync(a => a.ApplicationUserId == userId);
            var historicalOrderAddress = await verify.OrderHeaders.AsNoTracking()
                .Where(o => o.ApplicationUserId == userId).Select(o => o.StreetAddress).SingleAsync();

            Assert.Equal(2, addressCount);
            // Section 42 : les nouvelles adresses enregistrées n'affectent jamais le
            // snapshot d'une commande déjà passée.
            Assert.Equal("1 rue Historique", historicalOrderAddress);
        }

        [Fact]
        public async Task MultipleAddresses_OnlyOneDefault_PersistsAndReconstructsCorrectly()
        {
            if (SkipIfUnavailable()) return;

            var userId = await SeedUserAsync();
            using var context = _fixture.CreateBusinessContext();
            context.CustomerAddresses.Add(BuildAddress(userId, "Maison", isDefault: true));
            context.CustomerAddresses.Add(BuildAddress(userId, "Travail", isDefault: false));
            context.CustomerAddresses.Add(BuildAddress(userId, "Chalet", isDefault: false));
            await context.SaveChangesAsync();

            using var verify = _fixture.CreateBusinessContext();
            var addresses = await verify.CustomerAddresses.AsNoTracking()
                .Where(a => a.ApplicationUserId == userId).ToListAsync();

            Assert.Equal(3, addresses.Count);
            Assert.Single(addresses, a => a.IsDefaultShipping);
        }

        [Fact]
        public async Task SecondDefaultForSameUser_ViolatesUniqueFilteredIndex()
        {
            if (SkipIfUnavailable()) return;

            var userId = await SeedUserAsync();
            using var seedContext = _fixture.CreateBusinessContext();
            seedContext.CustomerAddresses.Add(BuildAddress(userId, "Maison", isDefault: true));
            await seedContext.SaveChangesAsync();

            // Contournement délibéré d'AddressService (accès direct au contexte) pour
            // prouver que la garantie vient du moteur (index unique filtré), pas
            // seulement du code applicatif — même méthodologie que
            // SqlServerConstraintTests pour les autres invariants du lot.
            using var context = _fixture.CreateBusinessContext();
            context.CustomerAddresses.Add(BuildAddress(userId, "Travail", isDefault: true));

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        [Fact]
        public async Task SetDefaultConcurrency_TwoSimultaneousRequests_NeverLeavesTwoDefaults()
        {
            if (SkipIfUnavailable()) return;

            var userId = await SeedUserAsync();
            int addressOneId, addressTwoId;
            using (var setupContext = _fixture.CreateBusinessContext())
            {
                // Aucune adresse par défaut au départ (contournement direct du contexte,
                // hors du chemin normal AddressService.CreateAsync) : les deux tâches
                // ci-dessous se disputent réellement le statut "par défaut", ce qui
                // exerce l'index unique filtré sous course concurrente au lieu de
                // dégénérer en no-op sur l'une des deux branches.
                var first = BuildAddress(userId, "Maison", isDefault: false);
                var second = BuildAddress(userId, "Travail", isDefault: false);
                setupContext.CustomerAddresses.Add(first);
                setupContext.CustomerAddresses.Add(second);
                await setupContext.SaveChangesAsync();
                addressOneId = first.Id;
                addressTwoId = second.Id;
            }

            using var contextA = _fixture.CreateBusinessContext();
            using var contextB = _fixture.CreateBusinessContext();
            var serviceA = new AddressService(contextA);
            var serviceB = new AddressService(contextB);

            // Les deux demandent à devenir l'adresse par défaut en même temps (deux
            // onglets, par exemple) — jamais deux par défaut à la fin, quel que soit
            // l'ordre d'arrivée.
            var taskA = serviceA.SetDefaultAsync(addressTwoId, userId);
            var taskB = serviceB.SetDefaultAsync(addressOneId, userId);
            await Task.WhenAll(taskA, taskB);

            using var verifyContext = _fixture.CreateBusinessContext();
            var defaultCount = await verifyContext.CustomerAddresses.AsNoTracking()
                .CountAsync(a => a.ApplicationUserId == userId && a.IsDefaultShipping);

            Assert.Equal(1, defaultCount);
        }
    }
}
