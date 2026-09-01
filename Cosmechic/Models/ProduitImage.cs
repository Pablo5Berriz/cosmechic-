namespace Cosmechic.Models;

// COSMECHIC-CATALOG-001 (section 30) : galerie d'images multiples par produit. Distincte
// de Produit.Image (conservé pour compatibilité ascendante — panier, historique de
// commande, vues existantes) : ce sont les images additionnelles/la galerie complète.
public partial class ProduitImage
{
    public int ProduitImageId { get; set; }

    public int ProduitId { get; set; }

    public string FileName { get; set; } = null!;

    public string? AltText { get; set; }

    public int SortOrder { get; set; }

    public bool IsPrimary { get; set; }

    public virtual Produit Produit { get; set; } = null!;
}
