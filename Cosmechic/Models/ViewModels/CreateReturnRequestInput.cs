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

    public class ReturnItemFormInput
    {
        public int OrderDetailId { get; set; }
        public int Quantity { get; set; }
        public string? Reason { get; set; }
    }
}
