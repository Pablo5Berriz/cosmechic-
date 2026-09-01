using System;
using System.Collections.Generic;

namespace Cosmechic.Models;

public partial class Category
{
    public int CategorieId { get; set; }

    public string Nom { get; set; } = null!;

    public string? Description { get; set; }

    public string Image { get; set; } = null!;

    public bool Disponible { get; set; }

    // COSMECHIC-CATALOG-001 (section 19) : identifiant URL stable, mêmes garanties que
    // Produit.Slug.
    public string? Slug { get; set; }

    public virtual ICollection<Produit> Produits { get; set; } = new List<Produit>();
}
