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
        // COSMECHIC-ACCOUNT-001 (section 15) : si renseigné, le serveur recopie le
        // snapshot depuis la CustomerAddress possédée par le client (jamais les champs
        // libres ci-dessous, ignorés dans ce cas) — sinon, adresse ponctuelle saisie
        // directement. Dans les deux cas, OrderHeader ne reçoit qu'un snapshot plat,
        // jamais une FK : modifier l'adresse enregistrée après coup ne change jamais une
        // commande déjà passée (section 42).
        public int? SelectedAddressId { get; set; }

        public string? Name { get; set; }
        public string? PhoneNumber { get; set; }
        public string? StreetAddress { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
        public int ShippingMethodId { get; set; }
    }
}
