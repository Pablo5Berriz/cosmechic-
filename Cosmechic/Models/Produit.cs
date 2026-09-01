using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

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
    // COSMECHIC-CATALOG-001 : [ValidateNever] — géré exclusivement par SQL Server
    // (rowversion), jamais par un formulaire. Sans cet attribut, la validation implicite
    // ASP.NET Core sur les types référence non-nullables rejetait TOUTE création de
    // produit via le formulaire Create (bug préexistant, jamais couvert par un test HTTP
    // avant ce lot : reproduit et corrigé ici en même temps que Categorie ci-dessous).
    [ValidateNever]
    public byte[] RowVersion { get; set; } = null!;

    // COSMECHIC-CATALOG-001 (section 16) : identifiant produit stable, indépendant du nom
    // (renommage, traduction future). Requis pour tout nouveau produit ; nullable en base
    // uniquement pour permettre le rétro-remplissage déterministe des lignes historiques
    // (voir CatalogBackfillService).
    public string? Sku { get; set; }

    // COSMECHIC-CATALOG-001 (section 17) : identifiant URL stable. Un renommage du produit
    // ne régénère jamais un slug existant (évite de casser des liens déjà publiés).
    public string? Slug { get; set; }

    public int? BrandId { get; set; }

    // COSMECHIC-CATALOG-001 (section 25/26, précision PM) : liste INCI telle que fournie
    // par le fabricant, affichée telle quelle — jamais interprétée/parsée en affirmation
    // santé.
    public string? IngredientsInci { get; set; }

    public string? UsageInstructions { get; set; }

    public string? Warnings { get; set; }

    // Quantité nette commerciale (ex. "250 ml", "50 g") : texte libre contrôlé par
    // l'admin, pas une unité de mesure structurée (hors périmètre de ce lot).
    public string? NetQuantity { get; set; }

    public string? SeoTitle { get; set; }

    public string? SeoDescription { get; set; }

    // COSMECHIC-CATALOG-001 (section 11) : requis pour un tri "nouveautés" réel plutôt que
    // simulé par l'ID. Rétro-rempli à la date de migration pour les lignes historiques.
    public DateTime DateCreation { get; set; }

    public virtual ICollection<Avi> Avis { get; set; } = new List<Avi>();

    // COSMECHIC-CATALOG-001 : [ValidateNever] — propriété de navigation, jamais liée
    // depuis un formulaire (seul CategorieId, la FK, l'est). Même correctif que
    // RowVersion ci-dessus.
    [ValidateNever]
    public virtual Category Categorie { get; set; } = null!;

    public virtual Brand? Brand { get; set; }

    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();

    public virtual ICollection<ProduitImage> Images { get; set; } = new List<ProduitImage>();
}
