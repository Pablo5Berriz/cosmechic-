using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Cosmechic.ModelBinding;

namespace Cosmechic.Models;

// COSMECHIC-COMMERCE-OPERATIONS-001A (section 14) : modèle configurable — le client
// envoie uniquement ShippingMethodId, jamais un montant ; le serveur charge le prix ici.
public partial class ShippingMethod
{
    public int ShippingMethodId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    [ModelBinder(BinderType = typeof(InvariantDecimalModelBinder))]
    public decimal Price { get; set; }

    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 15) : null = pas de livraison gratuite
    // pour cette méthode. Aucun seuil n'est inventé — configurable par l'admin,
    // TODO_REQUIRES_BUSINESS_CONFIGURATION tant qu'aucune valeur n'est saisie.
    [ModelBinder(BinderType = typeof(InvariantDecimalModelBinder))]
    public decimal? FreeShippingThreshold { get; set; }

    public bool IsActive { get; set; } = true;

    public int? EstimatedMinDays { get; set; }

    public int? EstimatedMaxDays { get; set; }

    public int SortOrder { get; set; }

    public virtual ICollection<OrderHeader> OrderHeaders { get; set; } = new List<OrderHeader>();
}
