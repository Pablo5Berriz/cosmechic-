using System;
using System.Collections.Generic;

namespace Cosmechic.Models;

public partial class OrderHeader
{
    public int Id { get; set; }

    public string ApplicationUserId { get; set; } = null!;

    public DateTime OrderDate { get; set; }

    public DateTime ShippingDate { get; set; }

    public decimal OrderTotal { get; set; }

    public string? OrderStatus { get; set; }

    public string? PaymentStatus { get; set; }

    public string? TrackingNumber { get; set; }

    public string? Carrier { get; set; }

    public DateTime PaymentDate { get; set; }

    public DateTime PaymentDueDate { get; set; }

    public string? SessionId { get; set; }

    public string? PaymentIntentId { get; set; }

    public string PhoneNumber { get; set; } = null!;

    public string StreetAddress { get; set; } = null!;

    public string City { get; set; } = null!;

    public string State { get; set; } = null!;

    public string PostalCode { get; set; } = null!;

    public string Name { get; set; } = null!;

    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 6/7) : snapshot financier complet.
    // OrderTotal reste le champ historique (ECOM-CORE-001) ; l'invariant
    // OrderTotal = Subtotal + ShippingAmount + TaxAmount - DiscountAmount est imposé par
    // une CHECK constraint SQL Server (CosmechicsContext), jamais recalculé depuis les
    // prix/taux courants pour une commande déjà passée (section 7).
    public decimal Subtotal { get; set; }

    public decimal ShippingAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    public int? ShippingMethodId { get; set; }

    // Snapshot (section 33) : une méthode de livraison désactivée/renommée après coup ne
    // doit jamais changer l'affichage d'une commande historique.
    public string? ShippingMethodName { get; set; }

    public virtual AspNetUser ApplicationUser { get; set; } = null!;

    public virtual ShippingMethod? ShippingMethod { get; set; }

    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
}
