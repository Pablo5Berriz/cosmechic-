using System.Net;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-IDENTITY-COMMS-001 (section 24) : simule un IEmailSender qui échoue
    // (fournisseur SMTP indisponible) et vérifie le comportement retenu (section 15) :
    // le compte reste créé mais non confirmé, aucune exception non gérée ne remonte au
    // client (pas de 500), aucun secret n'est exposé, et le renvoi de confirmation reste
    // disponible une fois le problème résolu.
    public class IdentityEmailFailureTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();
        private readonly HttpClient _client;

        public IdentityEmailFailureTests()
        {
            _client = _factory.CreateTestClient();
        }

        private static FormUrlEncodedContent RegisterForm(string userName, string email, string password) => new(new Dictionary<string, string>
        {
            ["Input.UserName"] = userName,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        });

        [Fact]
        public async Task Register_EmailSenderThrows_AccountStillCreatedUnconfirmed_NoUnhandled500()
        {
            _factory.EmailSender.ExceptionToThrow = new InvalidOperationException("simulated SMTP outage");

            var userName = $"user{Guid.NewGuid():N}";
            var email = $"{userName}@example.test";

            var response = await _client.PostAsync("/Identity/Account/Register", RegisterForm(userName, email, "P@ssword123!"));

            // Toujours redirigé vers RegisterConfirmation malgré l'échec d'envoi : pas de
            // 500 non géré exposé à l'utilisateur.
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("RegisterConfirmation", response.Headers.Location!.ToString());

            // Le compte est bien créé (jamais supprimé sur un simple échec d'envoi),
            // mais reste non confirmé.
            var user = _factory.QueryIdentity(ctx => ctx.Users.FirstOrDefault(u => u.UserName == userName));
            Assert.NotNull(user);
            Assert.False(user!.EmailConfirmed);

            // Aucun email capturé (l'envoi a échoué), donc aucun secret/contenu ne peut
            // avoir fuité par ce canal.
            Assert.Null(_factory.EmailSender.LastEmailTo(email));
        }

        [Fact]
        public async Task Register_EmailSenderThrows_ResendRemainsAvailableAfterProviderRecovers()
        {
            _factory.EmailSender.ExceptionToThrow = new InvalidOperationException("simulated SMTP outage");
            var userName = $"user{Guid.NewGuid():N}";
            var email = $"{userName}@example.test";
            await _client.PostAsync("/Identity/Account/Register", RegisterForm(userName, email, "P@ssword123!"));

            // Le fournisseur SMTP est de nouveau disponible.
            _factory.EmailSender.ExceptionToThrow = null;

            var resendResponse = await _client.PostAsync(
                "/Identity/Account/ResendEmailConfirmation",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Email"] = email }));

            Assert.Equal(HttpStatusCode.OK, resendResponse.StatusCode);
            var resentEmail = _factory.EmailSender.LastEmailTo(email);
            Assert.NotNull(resentEmail);
            Assert.Contains("ConfirmEmail", resentEmail!.HtmlBody);
        }

        [Fact]
        public async Task ForgotPassword_EmailSenderThrows_StillRedirectsToConfirmation_NoUnhandled500()
        {
            // Un utilisateur confirmé doit d'abord exister (ForgotPassword n'envoie un
            // email que pour un compte dont EmailConfirmed=true) — créé et confirmé sur
            // CE MÊME factory/base avant de simuler la panne SMTP.
            var userName = $"user{Guid.NewGuid():N}";
            var email = $"{userName}@example.test";
            await _client.PostAsync("/Identity/Account/Register", RegisterForm(userName, email, "P@ssword123!"));
            var confirmLink = FakeEmailSender.ExtractFirstLink(_factory.EmailSender.LastEmailTo(email)!);
            await _client.GetAsync(new Uri(confirmLink).PathAndQuery);

            _factory.EmailSender.ExceptionToThrow = new InvalidOperationException("simulated SMTP outage");

            var response = await _client.PostAsync(
                "/Identity/Account/ForgotPassword",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Email"] = email }));

            // Même en cas d'échec SMTP, ForgotPassword ne doit jamais renvoyer un 500 ni
            // révéler l'échec au client (comportement identique à un envoi réussi).
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("ForgotPasswordConfirmation", response.Headers.Location!.ToString());
        }

        public void Dispose() => _factory.Dispose();
    }
}
