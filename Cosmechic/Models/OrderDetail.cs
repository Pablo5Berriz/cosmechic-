using System;
using System.Collections.Generic;

namespace Cosmechic.Models;

public partial class OrderDetail
{
    public int Id { get; set; }

    public int OrderHeaderId { get; set; }

    public int ProduitId { get; set; }

    public int Count { get; set; }

    public decimal Price { get; set; }

    // Préparation COSMECHIC-DATA-001 pour l'intégrité historique des commandes : capture
    // du nom du produit tel qu'il était au moment de l'achat, indépendamment d'un
    // renommage ultérieur du produit vivant. Colonne ajoutée au schéma mais PAS ENCORE
    // renseignée par CartController.SummaryPOST (aucune modification du flux de commande
    // dans ce lot) — à raccorder explicitement dans COSMECHIC-ECOM-CORE-001.
    public string? ProduitNom { get; set; }

    public virtual OrderHeader OrderHeader { get; set; } = null!;

    public virtual Produit Produit { get; set; } = null!;
}
