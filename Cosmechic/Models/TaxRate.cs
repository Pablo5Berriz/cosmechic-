using System;

namespace Cosmechic.Models;

// COSMECHIC-COMMERCE-OPERATIONS-001A (section 21) : modèle DB plutôt qu'une simple
// configuration scalaire — justifié par le besoin réel de sommer plusieurs taux actifs
// pour une même juridiction (au Québec : TPS fédérale 5% + TVQ provinciale 9.975%, deux
// taux distincts déjà présents — en dur, uniquement à l'affichage — dans
// Views/Cart/Summary.cshtml avant ce lot). Ce lot ne construit que la mécanique ; aucune
// situation fiscale n'est inventée pour une juridiction non déjà établie dans le code.
public partial class TaxRate
{
    public int TaxRateId { get; set; }

    public string Jurisdiction { get; set; } = null!;

    public string CountryCode { get; set; } = null!;

    public string? RegionCode { get; set; }

    public decimal Rate { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;
}
