using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace Cosmechic.Tests.Infrastructure
{
    public record CapturedEmail(string Recipient, string Subject, string HtmlBody);

    // COSMECHIC-IDENTITY-COMMS-001 (section 21) : double de test pour IEmailSender.
    // N'envoie jamais de message externe — capture en mémoire (recipient/subject/body)
    // pour permettre aux tests d'en extraire le lien de confirmation/réinitialisation.
    public class FakeEmailSender : IEmailSender
    {
        private readonly ConcurrentQueue<CapturedEmail> _sent = new();

        public IReadOnlyCollection<CapturedEmail> SentEmails => _sent.ToArray();

        // Permet aux tests de simuler un échec de fournisseur SMTP (section 24).
        public Exception? ExceptionToThrow { get; set; }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            _sent.Enqueue(new CapturedEmail(email, subject, htmlMessage));
            return Task.CompletedTask;
        }

        public CapturedEmail? LastEmailTo(string recipient)
            => SentEmails.LastOrDefault(e => string.Equals(e.Recipient, recipient, StringComparison.OrdinalIgnoreCase));

        // Extrait le premier lien http(s) trouvé dans le corps HTML capturé (les emails
        // de ce projet ne contiennent qu'un seul lien d'action).
        public static string ExtractFirstLink(CapturedEmail email)
        {
            var match = System.Text.RegularExpressions.Regex.Match(email.HtmlBody, "href='(?<url>[^']+)'");
            if (!match.Success)
            {
                throw new InvalidOperationException("Aucun lien trouvé dans l'email capturé.");
            }
            return System.Net.WebUtility.HtmlDecode(match.Groups["url"].Value);
        }
    }
}
