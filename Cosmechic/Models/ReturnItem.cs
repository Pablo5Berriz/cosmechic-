using System;
using Cosmechic.Services;

namespace Cosmechic.Models;

// COSMECHIC-COMMERCE-OPERATIONS-001B (section 16/17/38/39) : une ligne par produit/quantité
// concerné dans une ReturnRequest. L'invariant "quantité retournée cumulée <= quantité
// achetée" se vérifie contre OrderDetail.Count et la somme des ReturnItem.Quantity déjà
// existants pour ce même OrderDetail (impossible à exprimer par une seule CHECK SQL —
// contrôlé applicativement, voir ReturnService).
//
// COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 5/6) : Category/IsOpened/IsUsed/
// CustomerDeclaredResellable vivent ici (par LIGNE), pas sur ReturnRequest — cohérent avec
// CanRequestReturnAsync qui évalue déjà l'éligibilité par OrderDetail, et avec le fait qu'une
// même demande de retour peut porter plusieurs produits dont l'état déclaré diffère (un
// scellé, un ouvert). Category est la seule source de vérité pour l'éligibilité et le
// routage — Reason (texte libre) ne détermine jamais rien par lui-même.
public partial class ReturnItem
{
    public int Id { get; set; }

    public int ReturnRequestId { get; set; }

    public int OrderDetailId { get; set; }

    public int Quantity { get; set; }

    public string? Reason { get; set; }

    public ReturnReasonCategory Category { get; set; }

    // Nullable par conception (section 6) : null = "non pertinent pour cette catégorie",
    // jamais confondu avec false ("le client a déclaré non ouvert"). Obligatoires et non-null
    // uniquement pour ChangeOfMind — voir ReturnService.CanRequestReturnAsync.
    public bool? IsOpened { get; set; }

    public bool? IsUsed { get; set; }

    // Déclaration du CLIENT uniquement (section 6) — ne représente jamais la décision
    // d'inspection admin réelle de remise en stock, qui reste portée par
    // ReturnItem.Restocked/RestockedAt, déjà distincts et jamais fusionnés avec ce champ.
    public bool? CustomerDeclaredResellable { get; set; }

    // Remise en stock : décision explicite et distincte du remboursement financier
    // (section 38) — jamais automatique. Restocked=true + RestockedAt une seule fois par
    // ligne (section 39/40), protégé par retry optimiste sur Produit.RowVersion.
    public bool Restocked { get; set; }

    public DateTime? RestockedAt { get; set; }

    public virtual ReturnRequest ReturnRequest { get; set; } = null!;

    public virtual OrderDetail OrderDetail { get; set; } = null!;
}
