namespace Cosmechic.Models.ViewModels
{
    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 41/44) : type de liaison dédié au POST de
    // Summary, distinct de ShoppingCartVM/OrderHeader. Ne porte QUE les champs légitimement
    // modifiables par le client (adresse de livraison + méthode choisie) : aucune valeur
    // financière (OrderTotal, Subtotal, ShippingAmount, TaxAmount, DiscountAmount) ni d'état
    // (PaymentStatus, OrderStatus, SessionId, PaymentIntentId, ApplicationUserId) n'existe
    // sur ce type — un POST contenant ces clés n'a donc littéralement aucune propriété où se
    // lier (contrairement à un [Bind(Exclude=...)] sur l'entité complète, qui resterait
    // fragile à l'ajout futur d'un champ sensible sur OrderHeader).
    public class CheckoutFormInput
    {
        public string? Name { get; set; }
        public string? PhoneNumber { get; set; }
        public string? StreetAddress { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
        public int ShippingMethodId { get; set; }
    }
}
