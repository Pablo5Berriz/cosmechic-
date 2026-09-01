using System.Net;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-IDENTITY-COMMS-001 (section 22) : preuve de bout en bout, à travers la
    // vraie pipeline HTTP ASP.NET Core, que l'inscription fonctionne réellement de bout
    // en bout maintenant que Register.cshtml.cs passe par IEmailSender (FakeEmailSender
    // en test, aucun email externe réel envoyé — section 37).
    public class IdentityRegistrationTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();
        private readonly HttpClient _client;

        public IdentityRegistrationTests()
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

        private static FormUrlEncodedContent LoginForm(string userName, string password) => new(new Dictionary<string, string>
        {
            ["Input.Username"] = userName,
            ["Input.Password"] = password,
        });

        [Fact]
        public async Task Register_CreatesUnconfirmedUser_SendsConfirmationEmail_ViaEmailSender()
        {
            var userName = $"user{Guid.NewGuid():N}";
            var email = $"{userName}@example.test";

            var response = await _client.PostAsync("/Identity/Account/Register", RegisterForm(userName, email, "P@ssword123!"));

            // Redirection vers RegisterConfirmation : inscription structurellement réussie.
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("RegisterConfirmation", response.Headers.Location!.ToString());

            var user = _factory.QueryIdentity(ctx => ctx.Users.FirstOrDefault(u => u.UserName == userName));
            Assert.NotNull(user);
            Assert.False(user!.EmailConfirmed);

            var sentEmail = _factory.EmailSender.LastEmailTo(email);
            Assert.NotNull(sentEmail);
            Assert.Contains("Confirm your email", sentEmail!.Subject);
            Assert.Contains("ConfirmEmail", sentEmail.HtmlBody);
        }

        [Fact]
        public async Task Register_Confirm_Login_FullFlow_Succeeds()
        {
            var userName = $"user{Guid.NewGuid():N}";
            var email = $"{userName}@example.test";
            const string password = "P@ssword123!";

            var registerResponse = await _client.PostAsync("/Identity/Account/Register", RegisterForm(userName, email, password));
            Assert.Equal(HttpStatusCode.Redirect, registerResponse.StatusCode);

            // Avant confirmation : la connexion doit être refusée (RequireConfirmedAccount).
            var loginBeforeConfirm = await _client.PostAsync("/Identity/Account/Login", LoginForm(userName, password));
            Assert.Equal(HttpStatusCode.OK, loginBeforeConfirm.StatusCode); // Page() ré-affichée, pas de redirection de succès
            var bodyBeforeConfirm = await loginBeforeConfirm.Content.ReadAsStringAsync();
            Assert.Contains("Tentative de connexion invalide", bodyBeforeConfirm);

            // Extrait le lien de confirmation depuis l'email capturé (jamais un vrai envoi).
            var sentEmail = _factory.EmailSender.LastEmailTo(email);
            Assert.NotNull(sentEmail);
            var confirmLink = FakeEmailSender.ExtractFirstLink(sentEmail!);
            var confirmPath = new Uri(confirmLink).PathAndQuery;

            var confirmResponse = await _client.GetAsync(confirmPath);
            Assert.True(
                confirmResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect,
                $"ConfirmEmail a retourné {confirmResponse.StatusCode}");

            var emailConfirmed = _factory.QueryIdentity(ctx => ctx.Users.First(u => u.UserName == userName).EmailConfirmed);
            Assert.True(emailConfirmed);

            // Après confirmation : la connexion doit désormais réussir.
            var loginAfterConfirm = await _client.PostAsync("/Identity/Account/Login", LoginForm(userName, password));
            Assert.Equal(HttpStatusCode.Redirect, loginAfterConfirm.StatusCode);
        }

        public void Dispose() => _factory.Dispose();
    }
}
