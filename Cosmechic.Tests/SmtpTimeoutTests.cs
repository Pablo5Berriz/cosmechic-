using System.Diagnostics;
using Cosmechic.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-QA-RELEASE-001 (section 15) : reproduit en environnement réel (Register
    // via Kestrel + Smtp:Host = sandbox.smtp.mailtrap.io injoignable dans ce sandbox) — la
    // requête restait bloquée ~100s (délai de connexion par défaut de MailKit) avant que
    // l'échec ne soit détecté. SmtpEmailSender borne désormais explicitement ce délai à
    // 15s. Ce test ne peut pas dépendre d'un vrai relais SMTP indisponible de façon fiable
    // en CI : il pointe vers une adresse RFC 5737 réservée à la documentation (jamais
    // routée sur Internet), qui provoque de façon fiable un délai d'attente de connexion
    // plutôt qu'un refus immédiat — même mécanisme que le SMTP réel injoignable observé.
    public class SmtpTimeoutTests
    {
        [Fact]
        public async Task SendEmailAsync_UnreachableHost_FailsWithinBoundedTime_NotMailKitDefault()
        {
            var settings = new SmtpSettings
            {
                Host = "192.0.2.1", // TEST-NET-1 (RFC 5737) : jamais routé, ne répond jamais.
                Port = 2525,
                FromAddress = "test@example.test",
                FromName = "Test",
                UseSsl = false,
            };
            var sender = new SmtpEmailSender(Options.Create(settings), NullLogger<SmtpEmailSender>.Instance);

            var stopwatch = Stopwatch.StartNew();
            var ex = await Record.ExceptionAsync(() => sender.SendEmailAsync("to@example.test", "Sujet", "<p>Corps</p>"));
            stopwatch.Stop();

            Assert.NotNull(ex);
            // Le délai par défaut de MailKit est ~100s ; la borne explicite (15s) doit
            // faire échouer la tentative très en-deçà de cette valeur.
            Assert.True(stopwatch.Elapsed.TotalSeconds < 30,
                $"La connexion SMTP a pris {stopwatch.Elapsed.TotalSeconds:0.0}s — le délai explicite de 15s ne semble pas appliqué.");
        }
    }
}
