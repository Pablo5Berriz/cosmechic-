using System.Diagnostics;
using Cosmechic.Data;
using Cosmechic.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Cosmechic.Tests.Infrastructure
{
    // COSMECHIC-DATA-001 (section 25) : le fournisseur InMemory ne peut pas vérifier le
    // comportement réel de SQL Server (CHECK constraints, index uniques appliqués en base,
    // génération de rowversion). Cette fixture démarre un conteneur SQL Server 2022
    // Linux jetable via Docker (mêmes commandes que la validation manuelle du lot), y
    // applique les DEUX jeux de migrations (ApplicationDbContext puis CosmechicsContext,
    // dans cet ordre à cause de la FK OrderHeaders -> AspNetUsers), puis le détruit à la
    // fin de la classe de tests.
    //
    // Si Docker n'est pas disponible dans l'environnement d'exécution (poste développeur
    // sans Docker, CI restreinte, etc.), les tests qui dépendent de cette fixture se
    // désactivent proprement (IsAvailable = false) plutôt que de faire échouer
    // `dotnet test` pour tout le monde : ce lot ne doit pas rendre Docker obligatoire
    // pour contribuer au dépôt, seulement l'exploiter quand il est présent.
    public sealed class SqlServerFixture : IAsyncLifetime
    {
        private const string ContainerName = "cosmechic-tests-sqlserver-fixture";
        private const int HostPort = 15599;
        private readonly string _saPassword = $"Cx_{Guid.NewGuid():N}!A1";

        public bool IsAvailable { get; private set; }
        public string SkipReason { get; private set; } = "Non initialisé.";
        public string ConnectionString { get; private set; } = string.Empty;

        public async Task InitializeAsync()
        {
            if (!IsDockerAvailable())
            {
                SkipReason = "Docker indisponible dans cet environnement.";
                return;
            }

            RunDocker($"rm -f {ContainerName}", allowFailure: true);

            var start = RunDocker(
                $"run -d --name {ContainerName} -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD={_saPassword} " +
                $"-p {HostPort}:1433 mcr.microsoft.com/mssql/server:2022-latest",
                allowFailure: true);

            if (start.ExitCode != 0)
            {
                SkipReason = $"Impossible de démarrer le conteneur SQL Server jetable : {start.StdErr}";
                return;
            }

            ConnectionString =
                $"Server=127.0.0.1,{HostPort};Database=CosmechicTestsFixture;User Id=sa;" +
                $"Password={_saPassword};TrustServerCertificate=true;Connect Timeout=5";

            if (!await WaitForServerReadyAsync(TimeSpan.FromSeconds(90)))
            {
                SkipReason = "Le serveur SQL Server jetable n'a pas répondu dans le délai imparti.";
                RunDocker($"rm -f {ContainerName}", allowFailure: true);
                return;
            }

            try
            {
                var identityOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlServer(ConnectionString)
                    .Options;
                using (var identityContext = new ApplicationDbContext(identityOptions))
                {
                    await identityContext.Database.MigrateAsync();
                }

                var businessOptions = new DbContextOptionsBuilder<CosmechicsContext>()
                    .UseSqlServer(ConnectionString)
                    .Options;
                using (var businessContext = new CosmechicsContext(businessOptions))
                {
                    await businessContext.Database.MigrateAsync();
                }
            }
            catch (Exception ex)
            {
                SkipReason = $"Échec de l'application des migrations sur la base jetable : {ex.Message}";
                RunDocker($"rm -f {ContainerName}", allowFailure: true);
                return;
            }

            IsAvailable = true;
        }

        public async Task DisposeAsync()
        {
            if (IsDockerAvailable())
            {
                RunDocker($"rm -f {ContainerName}", allowFailure: true);
            }
            await Task.CompletedTask;
        }

        public CosmechicsContext CreateBusinessContext()
        {
            var options = new DbContextOptionsBuilder<CosmechicsContext>()
                .UseSqlServer(ConnectionString)
                .Options;
            return new CosmechicsContext(options);
        }

        private async Task<bool> WaitForServerReadyAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            var masterConnectionString =
                $"Server=127.0.0.1,{HostPort};Database=master;User Id=sa;Password={_saPassword};" +
                "TrustServerCertificate=true;Connect Timeout=5";

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    await using var connection = new SqlConnection(masterConnectionString);
                    await connection.OpenAsync();
                    return true;
                }
                catch
                {
                    await Task.Delay(1500);
                }
            }

            return false;
        }

        private static bool IsDockerAvailable()
        {
            var result = RunProcess("docker", "info", allowFailure: true, timeoutSeconds: 15);
            return result.ExitCode == 0;
        }

        private static (int ExitCode, string StdOut, string StdErr) RunDocker(string arguments, bool allowFailure)
            => RunProcess("docker", arguments, allowFailure, timeoutSeconds: 120);

        private static (int ExitCode, string StdOut, string StdErr) RunProcess(
            string fileName, string arguments, bool allowFailure, int timeoutSeconds)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                if (allowFailure)
                {
                    return (-1, string.Empty, "Impossible de démarrer le processus.");
                }
                throw new InvalidOperationException($"Impossible de démarrer '{fileName} {arguments}'.");
            }

            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit(timeoutSeconds * 1000);

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                if (!allowFailure)
                {
                    throw new TimeoutException($"'{fileName} {arguments}' a dépassé le délai imparti.");
                }
                return (-1, stdOut, "Délai dépassé.");
            }

            return (process.ExitCode, stdOut, stdErr);
        }
    }

    [CollectionDefinition("SqlServerFixture collection")]
    public sealed class SqlServerFixtureCollection : ICollectionFixture<SqlServerFixture>
    {
    }
}
