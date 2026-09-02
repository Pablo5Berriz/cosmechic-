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

        // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 10/11/17E/17F) : preuve SQL
        // Server réelle des deux corrections de confidentialité — les champs legacy
        // AspNetUser (propriétés CLR réelles, pas fantômes) sont effacés, les lignes
        // ShoppingCart du compte anonymisé sont supprimées, un panier appartenant à un AUTRE
        // compte reste intact, et les commandes historiques restent intactes avec leur FK.
        [Fact]
        public async Task AnonymizeAsync_ClearsLegacyAspNetUserFields_DeletesOwnCart_PreservesOtherAccountCartAndOrders()
        {
            if (SkipIfUnavailable()) return;

            using var factory = CreateFactory();
            using var scope = factory.Services.CreateScope();
            var businessContext = scope.ServiceProvider.GetRequiredService<CosmechicsContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var anonymizationService = scope.ServiceProvider.GetRequiredService<IAccountAnonymizationService>();

            var userId = $"user-{Guid.NewGuid():N}";
            var otherUserId = $"user-{Guid.NewGuid():N}";
            var email = $"real-{Guid.NewGuid():N}@example.test";
            var otherEmail = $"real-{Guid.NewGuid():N}@example.test";
            var createResult = await userManager.CreateAsync(new IdentityUser { Id = userId, UserName = email, Email = email }, "Real!Passw0rd123");
            Assert.True(createResult.Succeeded, string.Join(";", createResult.Errors.Select(e => e.Description)));
            var createOtherResult = await userManager.CreateAsync(new IdentityUser { Id = otherUserId, UserName = otherEmail, Email = otherEmail }, "Real!Passw0rd123");
            Assert.True(createOtherResult.Succeeded, string.Join(";", createOtherResult.Errors.Select(e => e.Description)));

            // Écran admin AspNetUsersController (Cosmechic.Models.AspNetUser, CosmechicsContext)
            // — propriétés CLR réelles, jamais des propriétés fantômes.
            var legacyProfile = await businessContext.AspNetUsers.SingleAsync(u => u.Id == userId);
            legacyProfile.StreetAddress = "1 rue Legacy";
            legacyProfile.City = "Montreal";
            legacyProfile.State = "QC";
            legacyProfile.PostalCode = "H0H0H0";
            await businessContext.SaveChangesAsync();

            var category = new Category { Nom = $"Cat-{Guid.NewGuid():N}", Image = "c.jpg", Disponible = true };
            businessContext.Categories.Add(category);
            await businessContext.SaveChangesAsync();
            var produit = new Produit { Nom = $"P-{Guid.NewGuid():N}", CategorieId = category.CategorieId, Prix = 10m, Stock = 5, Disponible = true, Image = "p.jpg" };
            businessContext.Produits.Add(produit);
            await businessContext.SaveChangesAsync();

            businessContext.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = userId, ProduitId = produit.ProduitId, Count = 1 });
            businessContext.ShoppingCarts.Add(new ShoppingCart { ApplicationUserId = otherUserId, ProduitId = produit.ProduitId, Count = 3 });
            await businessContext.SaveChangesAsync();

            var order = new OrderHeader
            {
                ApplicationUserId = userId,
                OrderDate = DateTime.UtcNow,
                OrderTotal = 10m,
                Subtotal = 10m,
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

            // 1) Champs legacy effacés.
            var reloadedProfile = await businessContext.AspNetUsers.AsNoTracking().SingleAsync(u => u.Id == userId);
            Assert.Null(reloadedProfile.StreetAddress);
            Assert.Null(reloadedProfile.City);
            Assert.Null(reloadedProfile.State);
            Assert.Null(reloadedProfile.PostalCode);

            // 2) Panier du compte anonymisé supprimé.
            var ownCartRemaining = await businessContext.ShoppingCarts.CountAsync(c => c.ApplicationUserId == userId);
            Assert.Equal(0, ownCartRemaining);

            // 3) Panier d'un AUTRE compte intact — jamais touché.
            var otherCart = await businessContext.ShoppingCarts.AsNoTracking().SingleAsync(c => c.ApplicationUserId == otherUserId);
            Assert.Equal(3, otherCart.Count);

            // 4) Commande historique intacte, FK toujours valide (SQL Server aurait rejeté
            // toute violation de contrainte lors du SaveChanges ci-dessus).
            var reloadedOrder = await businessContext.OrderHeaders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            Assert.Equal(userId, reloadedOrder.ApplicationUserId);
            Assert.Equal(10m, reloadedOrder.OrderTotal);
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
