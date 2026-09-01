using Cosmechic.Data;
using Cosmechic.Models;
using Cosmechic.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cosmechic.Tests.Infrastructure
{
    // Host de test : réutilise le VRAI Program.cs / pipeline ASP.NET Core (donc les vrais
    // filtres [Authorize]/[Authorize(Roles=...)]), en substituant uniquement :
    //   - les deux DbContext SQL Server -> EF Core InMemory (base isolée par instance),
    //   - le schéma d'authentification par défaut -> TestAuthHandler,
    //   - IStripeCheckoutService -> FakeStripeCheckoutService (aucun appel réseau Stripe).
    // Aucune modification de Program.cs pour ces aspects : tout se fait ici, côté test.
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        // Secret de test fixe, jamais un vrai secret Stripe (COSMECHIC-ECOM-CORE-001,
        // section 37) : appsettings.json ne fournit qu'une chaîne vide en Stripe:WebhookSecret ;
        // les tests qui signent un payload webhook doivent utiliser exactement cette valeur.
        public const string TestWebhookSecret = "whsec_test_fixture_secret_do_not_use_in_prod";

        private readonly string _dbName = Guid.NewGuid().ToString();

        public FakeStripeCheckoutService StripeCheckoutService { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Stripe:WebhookSecret"] = TestWebhookSecret,
                });
            });

            builder.ConfigureServices(services =>
            {
                ReplaceDbContext<ApplicationDbContext>(services, _dbName + "-identity");
                ReplaceDbContext<CosmechicsContext>(services, _dbName + "-business");

                services.AddDistributedMemoryCache();

                services.RemoveAll<IStripeCheckoutService>();
                services.AddSingleton<IStripeCheckoutService>(StripeCheckoutService);

                services.RemoveAll<IAntiforgery>();
                services.AddSingleton<IAntiforgery, NoOpAntiforgery>();

                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        }

        private static void ReplaceDbContext<TContext>(IServiceCollection services, string dbName)
            where TContext : DbContext
        {
            services.RemoveAll<DbContextOptions<TContext>>();
            services.RemoveAll<TContext>();

            // Program.cs configure ApplicationDbContext/CosmechicsContext avec UseSqlServer :
            // ses registrations internes EF (fournisseur SqlServer) restent dans le même
            // IServiceCollection et entrent en conflit avec InMemory si on laisse EF choisir
            // son fournisseur interne par défaut ("plusieurs fournisseurs enregistrés"). On
            // donne donc à chaque DbContext de test son propre service provider interne,
            // dédié exclusivement à InMemory, isolé du reste.
            var inMemoryServices = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<TContext>(options =>
            {
                options.UseInMemoryDatabase(dbName);
                options.UseInternalServiceProvider(inMemoryServices);
                // CheckoutService/StripeFulfillmentService ouvrent une transaction
                // explicite (COSMECHIC-ECOM-CORE-001, section 18) : InMemory ne supporte
                // pas les transactions et lève par défaut une erreur de configuration.
                // La garantie transactionnelle réelle est vérifiée contre SQL Server
                // (SqlServerFixture), pas ici — on désactive seulement l'avertissement
                // pour permettre à ces tests HTTP de bout en bout de s'exécuter.
                options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
            });
        }

        // Accès scopé et sûr à la base InMemory pour arranger/vérifier l'état en test
        // (le contexte est correctement disposé, les données restent dans la base
        // InMemory nommée, indépendamment de la durée de vie de ce DbContext précis).
        public void Seed(Action<CosmechicsContext> seed)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CosmechicsContext>();
            seed(context);
            context.SaveChanges();
        }

        public T Query<T>(Func<CosmechicsContext, T> query)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CosmechicsContext>();
            return query(context);
        }

        // Redirections désactivées par défaut : les tests d'autorisation veulent le code
        // de statut immédiat de l'action appelée (401/403/404/302/200), pas le résultat
        // après avoir suivi une redirection.
        public HttpClient CreateTestClient()
        {
            return CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }
    }
}
