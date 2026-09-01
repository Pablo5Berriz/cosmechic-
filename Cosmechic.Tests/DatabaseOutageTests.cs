using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Cosmechic.Models;
using Cosmechic.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-SECURITY-002 (section 13) : reproduit puis prouve la correction d'un bug
    // connu où la page d'erreur elle-même s'effondre quand SQL Server est injoignable, car
    // _Layout.cshtml invoque ShoppingCartViewComponent (accès CosmechicsContext) sur TOUTES
    // les pages, y compris /Home/Error. Un utilisateur authentifié sans compteur de panier
    // déjà en session déclenche donc une DEUXIÈME exception à l'intérieur du pipeline de
    // gestion d'erreur lui-même.
    public class DatabaseOutageFactory : CustomWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CosmechicsContext>>();
                services.RemoveAll<CosmechicsContext>();
                services.AddDbContext<CosmechicsContext>(options =>
                    options.UseSqlServer(
                        "Server=127.0.0.1,1;Database=unreachable;Connect Timeout=1;TrustServerCertificate=true",
                        sql => sql.CommandTimeout(1)));
            });
        }
    }

    // Variante en environnement Production : exerce le vrai chemin UseExceptionHandler("/Home/Error")
    // (plutôt que UseDeveloperExceptionPage) déclenché par une page ordinaire qui échoue.
    public class DatabaseOutageProductionFactory : DatabaseOutageFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
        }
    }

    public class DatabaseOutageTests : IClassFixture<DatabaseOutageFactory>
    {
        private readonly DatabaseOutageFactory _factory;
        public DatabaseOutageTests(DatabaseOutageFactory factory) => _factory = factory;

        [Fact]
        public async Task ErrorPage_RendersGracefully_WhenDatabaseIsUnreachable()
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, "outage-test-user");

            var response = await client.GetAsync("/Home/Error");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Une erreur", body);
        }
    }

    public class DatabaseOutageProductionTests : IClassFixture<DatabaseOutageProductionFactory>
    {
        private readonly DatabaseOutageProductionFactory _factory;
        public DatabaseOutageProductionTests(DatabaseOutageProductionFactory factory) => _factory = factory;

        [Fact]
        public async Task OrdinaryPage_FallsBackToFriendlyErrorPage_WhenDatabaseIsUnreachable_InProduction()
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, "outage-test-user-prod");

            // Produits/Index interroge CosmechicsContext directement -> déclenche l'exception
            // d'origine que UseExceptionHandler("/Home/Error") doit intercepter.
            var response = await client.GetAsync("/Produits/Index?id=1");

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Une erreur", body);
            Assert.DoesNotContain("SqlException", body);
            Assert.DoesNotContain("Microsoft.Data.SqlClient", body);
        }
    }
}
