using Cosmechic.Data;
using Cosmechic.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Cosmechic.Tests
{
    // COSMECHIC-IDENTITY-COMMS-001 (section 25) : vérifie contre un vrai SQL Server 2022
    // jetable, reconstruit depuis zéro (ApplicationDbContext puis CosmechicsContext), que
    // le gap de schéma confirmé par COSMECHIC-ECOM-CORE-001 est réellement résolu — pas
    // seulement au niveau du schéma (déjà vérifié en section 9 du mandat), mais au niveau
    // des chemins runtime concrets utilisés par AspNetUsersController,
    // OrderHeadersController, AvisController et CartController.Summary GET.
    [Collection("SqlServerFixture collection")]
    public class IdentitySqlServerTests
    {
        private readonly Infrastructure.SqlServerFixture _fixture;
        private readonly ITestOutputHelper _output;

        public IdentitySqlServerTests(Infrastructure.SqlServerFixture fixture, ITestOutputHelper output)
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

        private async Task<string> CreateUserViaIdentityAsync(string streetAddress, string city, string state, string postalCode)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_fixture.ConnectionString)
                .Options;
            await using var identityContext = new ApplicationDbContext(options);

            var userId = $"user-{Guid.NewGuid():N}";
            var user = new IdentityUser { Id = userId, UserName = $"user{Guid.NewGuid():N}", Email = "test@example.test" };
            identityContext.Users.Add(user);
            await identityContext.SaveChangesAsync();

            // Renseigne les champs d'adresse via propriété fantôme (comme le ferait un
            // futur écran de profil Identity qui les exposerait un jour de façon typée).
            identityContext.Entry(user).Property("StreetAddress").CurrentValue = streetAddress;
            identityContext.Entry(user).Property("City").CurrentValue = city;
            identityContext.Entry(user).Property("State").CurrentValue = state;
            identityContext.Entry(user).Property("PostalCode").CurrentValue = postalCode;
            await identityContext.SaveChangesAsync();

            return userId;
        }

        [Fact]
        public async Task AddressFields_SetViaApplicationDbContext_ArePreserved_AndVisibleViaCosmechicsContext()
        {
            if (SkipIfUnavailable()) return;

            var userId = await CreateUserViaIdentityAsync("1 rue Test", "Montreal", "QC", "H0H0H0");

            using var businessContext = _fixture.CreateBusinessContext();
            // Chemin exact de CartController.Summary() GET / AspNetUsersController :
            // matérialisation complète de l'entité AspNetUser via CosmechicsContext.
            var user = await businessContext.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

            Assert.NotNull(user);
            Assert.Equal("1 rue Test", user!.StreetAddress);
            Assert.Equal("Montreal", user.City);
            Assert.Equal("QC", user.State);
            Assert.Equal("H0H0H0", user.PostalCode);
        }

        [Fact]
        public async Task AspNetUsersController_IndexQuery_WorksAgainstReconstructedDatabase()
        {
            if (SkipIfUnavailable()) return;

            await CreateUserViaIdentityAsync("2 rue Test", "Quebec", "QC", "G1G1G1");

            using var businessContext = _fixture.CreateBusinessContext();
            // Reproduit exactement AspNetUsersController.Index() : ToListAsync() sur le
            // DbSet complet.
            var users = await businessContext.AspNetUsers.ToListAsync();

            Assert.NotEmpty(users);
        }

        [Fact]
        public async Task OrderHeadersController_ApplicationUserSelectList_WorksAgainstReconstructedDatabase()
        {
            if (SkipIfUnavailable()) return;

            await CreateUserViaIdentityAsync("3 rue Test", "Laval", "QC", "H7H7H7");

            using var businessContext = _fixture.CreateBusinessContext();
            // Reproduit exactement OrderHeadersController.Create()/Edit() :
            // new SelectList(_context.AspNetUsers, "Id", "Id") — matérialisation complète
            // du DbSet en énumérant l'IQueryable.
            var users = businessContext.AspNetUsers.ToList();

            Assert.NotEmpty(users);
        }

        [Fact]
        public async Task AvisController_UserNameProjection_WorksAgainstReconstructedDatabase()
        {
            if (SkipIfUnavailable()) return;

            var userId = await CreateUserViaIdentityAsync("4 rue Test", "Gatineau", "QC", "J8J8J8");

            using var businessContext = _fixture.CreateBusinessContext();
            // Reproduit exactement AvisController : .Select(u => u.UserName) — cette
            // projection n'a jamais été affectée par le gap (colonnes non sélectionnées),
            // vérifié explicitement pour non-régression.
            var userName = await businessContext.AspNetUsers
                .Where(u => u.Id == userId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync();

            Assert.NotNull(userName);
        }

        [Fact]
        public async Task CartControllerSummaryGet_ApplicationUserLookup_WorksAgainstReconstructedDatabase()
        {
            if (SkipIfUnavailable()) return;

            var userId = await CreateUserViaIdentityAsync("5 rue Test", "Sherbrooke", "QC", "J1J1J1");

            using var businessContext = _fixture.CreateBusinessContext();
            // Reproduit exactement CartController.Summary() GET :
            // _context.AspNetUsers.Where(u => u.Id == userId).FirstOrDefault().
            var user = businessContext.AspNetUsers.Where(u => u.Id == userId).FirstOrDefault();

            Assert.NotNull(user);
            Assert.NotNull(user!.UserName);
        }
    }
}
