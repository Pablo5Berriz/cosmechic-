using System.Collections.Generic;

namespace Cosmechic.Models;

// COSMECHIC-CATALOG-001 (section 20) : entité réelle plutôt qu'une simple chaîne — la
// marque est filtrable, administrable et destinée à un futur usage SEO par marque.
public partial class Brand
{
    public int BrandId { get; set; }

    public string Nom { get; set; } = null!;

    // Nullable côté C# (bien que NOT NULL en base, voir CosmechicsContext) : le slug est
    // toujours calculé côté serveur (jamais saisi via le formulaire Create/Edit) — le
    // rendre non-nullable déclencherait la validation implicite ASP.NET Core sur les
    // types référence non-nullables avant même que le contrôleur ait pu le renseigner.
    public string? Slug { get; set; }

    // Désactivation uniquement (jamais de suppression physique depuis l'admin) : une
    // marque référencée par des produits ne doit jamais casser silencieusement leur
    // navigation.
    public bool Disponible { get; set; } = true;

    public virtual ICollection<Produit> Produits { get; set; } = new List<Produit>();
}
