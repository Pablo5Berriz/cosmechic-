using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cosmechic.Tests.Infrastructure
{
    // Handler d'authentification de test : construit un ClaimsPrincipal à partir d'en-têtes
    // HTTP dédiés au lieu du cookie Identity réel, tout en passant par le VRAI pipeline
    // ASP.NET Core ([Authorize], [Authorize(Roles=...)], User.IsInRole(...) sont donc
    // réellement évalués). Sans en-tête "X-Test-UserId" -> requête anonyme.
    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";
        public const string UserIdHeader = "X-Test-UserId";
        public const string UserNameHeader = "X-Test-UserName";
        public const string RolesHeader = "X-Test-Roles";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserIdHeader, out var userId) || string.IsNullOrEmpty(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId!),
                new(ClaimTypes.Name, Request.Headers.TryGetValue(UserNameHeader, out var name) ? name.ToString() : userId!),
            };

            if (Request.Headers.TryGetValue(RolesHeader, out var roles) && !string.IsNullOrEmpty(roles))
            {
                foreach (var role in roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    claims.Add(new Claim(ClaimTypes.Role, role.Trim()));
                }
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        // Réponses HTTP simples et sans ambiguïté pour les tests : pas de redirection vers
        // une page de login/AccessDenied (comportement normal d'Identity en production,
        // inchangé), juste le code de statut brut à asserter.
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }
}
