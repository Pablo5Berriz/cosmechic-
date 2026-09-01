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

    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 2/42) : dimension distincte de
    // OrderStatus/PaymentStatus, uniquement mutée par IOrderLifecycleService.
    public string? FulfillmentStatus { get; set; }

    // TrackingNumber/Carrier (existants, section 42) : jamais renseignés par le code
    // avant ce lot ; désormais mis à jour par l'action admin "marquer expédiée"
    // (OrdersLifecycleController), aux côtés de ShippedAt.
    public DateTime? ShippedAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 25/34) : total réservé/effectivement
    // remboursé, maintenu comme un total glissant mis à jour atomiquement (avec
    // RowVersion) par RefundOrchestrationService — jamais recalculé à la volée par simple
    // somme applicative, pour permettre une contrainte CHECK moteur
    // (CK_OrderHeaders_RefundedAmount_WithinTotal) et une garde de concurrence réelle.
    public decimal RefundedAmount { get; set; }

    // Jeton de concurrence optimiste (section 34/71) : protège RefundedAmount contre deux
    // demandes de remboursement concurrentes qui, prises isolément, tiendraient chacune
    // dans le solde remboursable mais dépasseraient ensemble OrderTotal.
    public byte[]? RowVersion { get; set; }

    public virtual AspNetUser ApplicationUser { get; set; } = null!;

    public virtual ShippingMethod? ShippingMethod { get; set; }

    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();

    public virtual ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();

    public virtual ICollection<ReturnRequest> ReturnRequests { get; set; } = new List<ReturnRequest>();

    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}
