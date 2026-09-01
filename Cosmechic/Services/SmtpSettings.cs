namespace Cosmechic.Services
{
    // COSMECHIC-IDENTITY-COMMS-001 : configuration du seul point d'envoi email de
    // l'application (SmtpEmailSender). Toutes les valeurs viennent de la configuration
    // (appsettings / variables d'environnement / user-secrets) — jamais de credential en
    // dur dans le code.
    public class SmtpSettings
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public bool UseSsl { get; set; }
    }
}
