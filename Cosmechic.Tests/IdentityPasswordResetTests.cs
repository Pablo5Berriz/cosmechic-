using System.Net;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-IDENTITY-COMMS-001 (section 23) : preuve de bout en bout que
    // ForgotPassword/ResetPassword fonctionnent réellement maintenant qu'ils passent par
    // IEmailSender (aucun email externe réel — section 37).
    public class IdentityPasswordResetTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();
        private readonly HttpClient _client;

        public IdentityPasswordResetTests()
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

        private static FormUrlEncodedContent ForgotPasswordForm(string email) => new(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
        });

        private static FormUrlEncodedContent ResetPasswordForm(string email, string code, string newPassword) => new(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Code"] = code,
            ["Input.Password"] = newPassword,
            ["Input.ConfirmPassword"] = newPassword,
        });

        private static FormUrlEncodedContent LoginForm(string userName, string password) => new(new Dictionary<string, string>
        {
            ["Input.Username"] = userName,
            ["Input.Password"] = password,
        });

        private async Task<(string UserName, string Email)> RegisterAndConfirmAsync(string password)
        {
            var userName = $"user{Guid.NewGuid():N}";
            var email = $"{userName}@example.test";

            await _client.PostAsync("/Identity/Account/Register", RegisterForm(userName, email, password));

            var confirmLink = FakeEmailSender.ExtractFirstLink(_factory.EmailSender.LastEmailTo(email)!);
            await _client.GetAsync(new Uri(confirmLink).PathAndQuery);

            return (userName, email);
        }

        [Fact]
        public async Task ForgotPassword_KnownEmail_Reset_OldPasswordInvalid_NewPasswordValid()
        {
            const string oldPassword = "P@ssword123!";
            const string newPassword = "N3wP@ssword456!";
            var (userName, email) = await RegisterAndConfirmAsync(oldPassword);

            var forgotResponse = await _client.PostAsync("/Identity/Account/ForgotPassword", ForgotPasswordForm(email));
            Assert.Equal(HttpStatusCode.Redirect, forgotResponse.StatusCode);
            Assert.Contains("ForgotPasswordConfirmation", forgotResponse.Headers.Location!.ToString());

            var resetEmail = _factory.EmailSender.LastEmailTo(email);
            Assert.NotNull(resetEmail);
            Assert.Contains("Reset Password", resetEmail!.Subject);
            var resetLink = FakeEmailSender.ExtractFirstLink(resetEmail);
            var encodedCode = System.Web.HttpUtility.ParseQueryString(new Uri(resetLink).Query)["code"];
            Assert.NotNull(encodedCode);
            // ResetPassword.OnGet décode le code base64url avant de le stocker dans
            // Input.Code (le formulaire soumet ensuite la valeur déjà décodée) ; ce test
            // poste directement sans passer par le GET, donc il doit reproduire ce même
            // décodage lui-même.
            var code = System.Text.Encoding.UTF8.GetString(
                Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(encodedCode!));

            var resetResponse = await _client.PostAsync("/Identity/Account/ResetPassword", ResetPasswordForm(email, code, newPassword));
            Assert.Equal(HttpStatusCode.Redirect, resetResponse.StatusCode);
            Assert.Contains("ResetPasswordConfirmation", resetResponse.Headers.Location!.ToString());

            var loginWithOldPassword = await _client.PostAsync("/Identity/Account/Login", LoginForm(userName, oldPassword));
            Assert.Equal(HttpStatusCode.OK, loginWithOldPassword.StatusCode);
            var oldPasswordBody = await loginWithOldPassword.Content.ReadAsStringAsync();
            Assert.Contains("Tentative de connexion invalide", oldPasswordBody);

            var loginWithNewPassword = await _client.PostAsync("/Identity/Account/Login", LoginForm(userName, newPassword));
            Assert.Equal(HttpStatusCode.Redirect, loginWithNewPassword.StatusCode);
        }

        [Fact]
        public async Task ForgotPassword_UnknownEmail_DoesNotRevealAccountExistence()
        {
            var unknownEmail = $"nobody-{Guid.NewGuid():N}@example.test";

            var response = await _client.PostAsync("/Identity/Account/ForgotPassword", ForgotPasswordForm(unknownEmail));

            // Même redirection que pour un email connu (section 18) : aucune différence
            // observable côté client entre compte existant et compte inexistant.
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("ForgotPasswordConfirmation", response.Headers.Location!.ToString());
            Assert.DoesNotContain(_factory.EmailSender.SentEmails, e => e.Recipient == unknownEmail);
        }

        public void Dispose() => _factory.Dispose();
    }
}
