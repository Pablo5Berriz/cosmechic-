using Cosmechic.Data;
using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Cosmechic.Tests
{
    // COSMECHIC-BUSINESS-POLICY-001 (section 7/13) : l'anonymisation de compte touche des
    // FK réelles entre AspNetUsers et les données commerciales (OrderHeader.ApplicationUserId
    // non-nullable, CustomerAddress.ApplicationUserId ON DELETE CASCADE) — le fournisseur
    // InMemory ne les applique pas du tout, donc "aucune FK cassée" n'est vérifiable que
    // contre un vrai SQL Server. Utilise le même conteneur jetable que
    // SqlServerRefundAndRestockConcurrencyTests (SqlServerFixture) mais réutilise le VRAI
    // pipeline DI de Program.cs (Identity + UserManager complets) via une factory dédiée,
    // plutôt que de reconstruire un UserManager à la main.
    public class AccountAnonymizationSqlServerFactory : CustomWebApplicationFactory
    {
        public string ConnectionString { get; set; } = string.Empty;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<ApplicationDbContext>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(ConnectionString));

                services.RemoveAll<DbContextOptions<CosmechicsContext>>();
                services.RemoveAll<CosmechicsContext>();
                services.AddDbContext<CosmechicsContext>(options => options.UseSqlServer(ConnectionString));
            });
        }
    }

    [Collection("SqlServerFixture collection")]
    public class AccountAnonymizationSqlServerTests : IDisposable
    {
        private readonly SqlServerFixture _fixture;
        private readonly ITestOutputHelper _output;
        private AccountAnonymizationSqlServerFactory? _factory;

        public AccountAnonymizationSqlServerTests(SqlServerFixture fixture, ITestOutputHelper output)
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

        private AccountAnonymizationSqlServerFactory CreateFactory()
        {
            _factory = new AccountAnonymizationSqlServerFactory { ConnectionString = _fixture.ConnectionString };
            return _factory;
        }

        [Fact]
        public async Task AnonymizeAsync_UserWithOrderHistory_PreservesOrderHistory_BreaksNoForeignKey()
        {
            if (SkipIfUnavailable()) return;

            using var factory = CreateFactory();
            using var scope = factory.Services.CreateScope();
            var identityContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var businessContext = scope.ServiceProvider.GetRequiredService<CosmechicsContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var anonymizationService = scope.ServiceProvider.GetRequiredService<IAccountAnonymizationService>();

            var userId = $"user-{Guid.NewGuid():N}";
            var originalEmail = $"real-{Guid.NewGuid():N}@example.test";
            var user = new IdentityUser { Id = userId, UserName = originalEmail, Email = originalEmail, EmailConfirmed = true };
            var createResult = await userManager.CreateAsync(user, "Real!Passw0rd123");
            Assert.True(createResult.Succeeded, string.Join(";", createResult.Errors.Select(e => e.Description)));

            businessContext.CustomerAddresses.Add(new CustomerAddress
            {
                ApplicationUserId = userId,
                Label = "Maison",
                RecipientName = "Vrai Nom",
                PhoneNumber = "5145551234",
                StreetAddress = "1 rue Réelle",
                City = "Montreal",
                State = "QC",
                PostalCode = "H0H0H0",
                CountryCode = "CA",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });

            var order = new OrderHeader
            {
                ApplicationUserId = userId,
                OrderDate = DateTime.UtcNow,
                OrderTotal = 50m,
                Subtotal = 50m,
                Name = "Vrai Nom",
                PhoneNumber = "5145551234",
                StreetAddress = "1 rue Réelle",
                City = "Montreal",
                State = "QC",
                PostalCode = "H0H0H0",
            };
            businessContext.OrderHeaders.Add(order);
            await businessContext.SaveChangesAsync();
            var orderId = order.Id;

            var anonymized = await anonymizationService.AnonymizeAsync(userId);
            Assert.True(anonymized);

            // 1) Aucun hard-delete, aucune FK cassée : la commande existe toujours et
            // pointe toujours vers le MÊME userId.
            var reloadedOrder = await businessContext.OrderHeaders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            Assert.Equal(userId, reloadedOrder.ApplicationUserId);
            Assert.Equal(50m, reloadedOrder.OrderTotal); // snapshot financier intact.
            Assert.Equal(50m, reloadedOrder.Subtotal);

            // 2) Champs directement identifiants du snapshot anonymisés, ville/province/CP
            // conservés (audit fiscal).
            Assert.Equal("Client anonymisé", reloadedOrder.Name);
            Assert.NotEqual("5145551234", reloadedOrder.PhoneNumber);
            Assert.Equal("Montreal", reloadedOrder.City);
            Assert.Equal("QC", reloadedOrder.State);

            // 3) Carnet d'adresses supprimé.
            var remainingAddresses = await businessContext.CustomerAddresses.CountAsync(a => a.ApplicationUserId == userId);
            Assert.Equal(0, remainingAddresses);

            // 4) Identité anonymisée, non réversible, non identifiante.
            var reloadedUser = await userManager.FindByIdAsync(userId);
            Assert.NotNull(reloadedUser);
            Assert.NotEqual(originalEmail, reloadedUser!.Email);
            Assert.EndsWith("@anonymized.invalid", reloadedUser.Email);

            // 5) Reconnexion impossible : verrouillage permanent + mot de passe d'origine
            // ne fonctionne plus.
            Assert.True(await userManager.IsLockedOutAsync(reloadedUser));
            var oldPasswordStillWorks = await userManager.CheckPasswordAsync(reloadedUser, "Real!Passw0rd123");
            Assert.False(oldPasswordStillWorks);
        }

        [Fact]
        public async Task AnonymizeAsync_UnknownUser_ReturnsFalse()
        {
            if (SkipIfUnavailable()) return;

            using var factory = CreateFactory();
            using var scope = factory.Services.CreateScope();
            var anonymizationService = scope.ServiceProvider.GetRequiredService<IAccountAnonymizationService>();

            var result = await anonymizationService.AnonymizeAsync($"no-such-user-{Guid.NewGuid():N}");

            Assert.False(result);
        }

        public void Dispose() => _factory?.Dispose();
    }
}
