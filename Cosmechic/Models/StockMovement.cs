using System;

namespace Cosmechic.Models;

// COSMECHIC-COMMERCE-OPERATIONS-001B (section 41) : piste d'audit minimale de toute
// mutation de Produit.Stock, introduite ici puisque les retours ajoutent désormais une
// mutation POSITIVE (restock) à côté de la seule mutation négative existante
// (fulfillment). Chaque site qui mute Stock écrit également une ligne ici, dans la même
// transaction — jamais une reconstruction a posteriori.
public partial class StockMovement
{
    public int Id { get; set; }

    public int ProduitId { get; set; }

    public decimal QuantityDelta { get; set; }

    public string Reason { get; set; } = null!;

    public int? OrderId { get; set; }

    public int? ReturnItemId { get; set; }

    public string? ActorUserId { get; set; }

    public string ActorType { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Produit Produit { get; set; } = null!;
}
