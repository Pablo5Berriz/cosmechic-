using System.ComponentModel.DataAnnotations;

namespace Cosmechic.Models.ViewModels
{
    // COSMECHIC-CONTENT-LEGAL-001 (section 9) : DTO étroit du formulaire Contact public.
    // Ne porte aucun champ administratif — jamais lié à une entité.
    public class ContactMessageInput
    {
        [Required]
        [StringLength(200, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(4000, MinimumLength = 1)]
        public string Message { get; set; } = string.Empty;
    }
}
