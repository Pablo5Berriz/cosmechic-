using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Cosmechic.Tests.Infrastructure
{
    // La protection CSRF ([ValidateAntiForgeryToken]) est un mécanisme distinct du
    // contrôle d'accès/ownership que ce lot doit prouver automatiquement (section 16).
    // Elle est vérifiée par revue de code (section 13/K du rapport), pas par ces tests.
    // Ce double neutralise uniquement la validation antiforgery côté hôte de test, pour
    // isoler la preuve d'autorisation sans avoir à rejouer la mécanique cookie+jeton.
    public class NoOpAntiforgery : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext)
            => new("test-request-token", "test-cookie-token", "form", "header");

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext)
            => new("test-request-token", "test-cookie-token", "form", "header");

        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);

        public void SetCookieTokenAndHeader(HttpContext httpContext)
        {
        }

        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
    }
}
