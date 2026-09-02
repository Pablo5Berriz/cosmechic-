using Cosmechic.Services;

namespace Cosmechic.Models.ViewModels
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 54) : DTO ciblé pour la création d'une
    // demande de retour — ne porte jamais ApplicationUserId (toujours dérivé de l'identité
    // authentifiée côté serveur, jamais du formulaire) ni aucun champ financier/état.
    public class CreateReturnRequestInput
    {
        public int OrderId { get; set; }
        public string? Reason { get; set; }
        public string? CustomerComment { get; set; }
        public List<ReturnItemFormInput> Items { get; set; } = new();
    }

    // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 5/6/15) : DTO toujours étroit —
    // Category est un vrai enum (le binder MVC ne peut matérialiser qu'une des valeurs
    // définies, jamais une chaîne arbitraire), IsOpened/IsUsed/CustomerDeclaredResellable ne
    // sont exigés que pour ChangeOfMind (validation côté serveur dans ReturnService, jamais
    // côté client uniquement). Aucun champ financier, aucun champ de workflow interne
    // (Status/Restocked/RestockedAt) n'est exposé ici — un overposting ne peut donc jamais
    // affecter autre chose que ces quatre champs.
    public class ReturnItemFormInput
    {
        public int OrderDetailId { get; set; }
        public int Quantity { get; set; }
        public string? Reason { get; set; }
        public ReturnReasonCategory Category { get; set; }
        public bool? IsOpened { get; set; }
        public bool? IsUsed { get; set; }
        public bool? CustomerDeclaredResellable { get; set; }
    }
}
