using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Cosmechic.Services
{
    // COSMECHIC-IDENTITY-COMMS-001 (sections 11-14) : unique implémentation réelle de
    // IEmailSender de l'application, backée par MailKit/SMTP configuré. Remplace le
    // EmailSender par défaut (no-op) fourni par Microsoft.AspNetCore.Identity.UI, et les
    // deux implémentations ad hoc qui construisaient leur propre SmtpClient directement
    // dans Register.cshtml.cs et ForgotPassword.cshtml.cs (dont l'une avec des
    // identifiants placeholder utilisés comme logique d'exécution réelle).
    //
    // Ne masque jamais un échec d'envoi : une exception de connexion/authentification/
    // envoi SMTP remonte à l'appelant tel quel (transport pur). C'est à l'appelant
    // (page Identity) de décider comment se comporter côté UX en cas d'échec — voir
    // Register.cshtml.cs / ForgotPassword.cshtml.cs.
    public class SmtpEmailSender(IOptions<SmtpSettings> options, ILogger<SmtpEmailSender> logger) : IEmailSender
    {
        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var settings = options.Value;
            if (string.IsNullOrWhiteSpace(settings.Host))
            {
                // Erreur de configuration maîtrisée (section 14/15) plutôt qu'un échec de
                // connexion MailKit confus vers un hôte vide.
                throw new InvalidOperationException(
                    "Envoi d'email impossible : Smtp:Host n'est pas configuré.");
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
            message.To.Add(new MailboxAddress("", email));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlMessage };

            using var client = new SmtpClient();
            var socketOptions = settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(settings.Host, settings.Port, socketOptions);
            try
            {
                if (!string.IsNullOrEmpty(settings.Username))
                {
                    await client.AuthenticateAsync(settings.Username, settings.Password);
                }

                await client.SendAsync(message);
            }
            finally
            {
                await client.DisconnectAsync(true);
            }

            // Ne jamais loguer le contenu du message (peut contenir un lien de
            // confirmation/réinitialisation) ni les identifiants SMTP.
            logger.LogInformation("Email envoyé à {Recipient} (sujet : {Subject})", email, subject);
        }
    }
}
