using System;

namespace Cosmechic.Models;

// COSMECHIC-COMMERCE-OPERATIONS-001B (section 16/17/38/39) : une ligne par produit/quantité
// concerné dans une ReturnRequest. L'invariant "quantité retournée cumulée <= quantité
// achetée" se vérifie contre OrderDetail.Count et la somme des ReturnItem.Quantity déjà
// existants pour ce même OrderDetail (impossible à exprimer par une seule CHECK SQL —
// contrôlé applicativement, voir ReturnService).
public partial class ReturnItem
{
    public int Id { get; set; }

    public int ReturnRequestId { get; set; }

    public int OrderDetailId { get; set; }

    public int Quantity { get; set; }

    public string? Reason { get; set; }

    // Remise en stock : décision explicite et distincte du remboursement financier
    // (section 38) — jamais automatique. Restocked=true + RestockedAt une seule fois par
    // ligne (section 39/40), protégé par retry optimiste sur Produit.RowVersion.
    public bool Restocked { get; set; }

    public DateTime? RestockedAt { get; set; }

    public virtual ReturnRequest ReturnRequest { get; set; } = null!;

    public virtual OrderDetail OrderDetail { get; set; } = null!;
}
