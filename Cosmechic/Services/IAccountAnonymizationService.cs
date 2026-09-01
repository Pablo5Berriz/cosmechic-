namespace Cosmechic.Services
{
    // COSMECHIC-BUSINESS-POLICY-001 (section 7) : ACCOUNT_DELETION_ANONYMIZATION_POLICY
    // approuvée par le PM = anonymiser les données personnelles tout en conservant les
    // enregistrements transactionnels requis (commandes, remboursements, retours, audit).
    // Ne fait jamais de hard-delete d'un compte ayant un historique de commandes — voir
    // AccountAnonymizationService pour le détail exact de ce qui est anonymisé/conservé.
    public interface IAccountAnonymizationService
    {
        // Retourne false si l'utilisateur n'existe pas. Idempotent en pratique (ré-exécuter
        // sur un compte déjà anonymisé ne fait que réappliquer les mêmes valeurs).
        Task<bool> AnonymizeAsync(string userId);
    }
}
