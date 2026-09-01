namespace Cosmechic.Models.ViewModels
{
    // COSMECHIC-ACCOUNT-001 (section 7/26) : DTO étroit pour l'édition administrative
    // d'un profil AspNetUser. Volontairement SANS UserName/Email : les modifier
    // correctement exige UserManager.SetUserNameAsync/SetEmailAsync (synchronisation de
    // NormalizedUserName/NormalizedEmail, unicité, ré-confirmation) — un second système
    // que ce lot ne construit pas (section 8). Volontairement SANS aucun champ Identity
    // sensible (PasswordHash/SecurityStamp/ConcurrencyStamp/EmailConfirmed/
    // PhoneNumberConfirmed/TwoFactorEnabled/LockoutEnd/LockoutEnabled/AccessFailedCount) —
    // jamais modifiables par ce formulaire, même par un Admin.
    public class AspNetUserEditInput
    {
        public string Id { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? StreetAddress { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
    }
}
