using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Cosmechic.Models;

namespace Cosmechic.Tests.Infrastructure
{
    // Fabrique partagée pour un CosmechicsContext InMemory isolé, utilisée par les tests
    // qui instancient les services applicatifs directement (sans passer par
    // CustomWebApplicationFactory/WebApplicationFactory). Centralise les deux mêmes
    // contournements que CustomWebApplicationFactory : service provider interne dédié
    // (évite le conflit "plusieurs fournisseurs enregistrés" avec SqlServer) et
    // suppression de l'avertissement transaction (CheckoutService/StripeFulfillmentService
    // ouvrent une transaction explicite, non supportée par InMemory).
    public static class InMemoryContextFactory
    {
        public static CosmechicsContext Create()
        {
            var inMemoryServices = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            var options = new DbContextOptionsBuilder<CosmechicsContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .UseInternalServiceProvider(inMemoryServices)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new CosmechicsContext(options);
        }
    }
}
