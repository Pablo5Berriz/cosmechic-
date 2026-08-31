using System;
using System.Collections.Generic;

namespace Cosmechic.Models;

public partial class Produit
{
    public int ProduitId { get; set; }

    public string Nom { get; set; } = null!;

    public int CategorieId { get; set; }

    public string? Description { get; set; }

    public decimal Prix { get; set; }

    public decimal Stock { get; set; }

    public bool Disponible { get; set; }

    public string Image { get; set; } = null!;

    public int NombreVentes { get; set; }

    // Préparation COSMECHIC-DATA-001 pour COSMECHIC-ECOM-CORE-001 : jeton de concurrence
    // optimiste, nécessaire pour empêcher deux commandes concurrentes de survendre le
    // dernier exemplaire d'un produit (aucune logique de réservation/décrément n'est
    // ajoutée dans ce lot, seul le champ est préparé).
    public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<Avi> Avis { get; set; } = new List<Avi>();

    public virtual Category Categorie { get; set; } = null!;

    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
}
