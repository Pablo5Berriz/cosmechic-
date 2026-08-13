using System;
using System.Collections.Generic;

namespace Cosmechic.Models;

public partial class ShoppingCart
{
    public int Id { get; set; }

    public int ProduitId { get; set; }

    public int Count { get; set; }

    public string? ApplicationUserId { get; set; }

    public virtual Produit Produit { get; set; }

    public decimal Prix => Produit != null ? Produit.Prix : 0;
}
