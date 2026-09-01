using System;
using System.Collections.Generic;

namespace Cosmechic.Models;

// COSMECHIC-ACCOUNT-001 (section 10/11) : les 4 champs plats StreetAddress/City/State/
// PostalCode sur AspNetUsers (shadow properties, ApplicationDbContext) ne sont utilisés
// nulle part dans le checkout actuel (CartController.Summary ne préremplit que Name/
// PhoneNumber) et ne supportent qu'une seule adresse. CustomerAddress les remplace pour
// l'usage client réel : plusieurs adresses nommées, une adresse de livraison par défaut
// au plus. OrderHeader continue de ne stocker qu'un snapshot plat (aucune FK vers cette
// table) : modifier ou supprimer une CustomerAddress après coup ne change jamais une
// commande historique (section 15/42).
public partial class CustomerAddress
{
    public int Id { get; set; }

    public string ApplicationUserId { get; set; } = null!;

    public string Label { get; set; } = null!;

    public string RecipientName { get; set; } = null!;

    public string PhoneNumber { get; set; } = null!;

    public string StreetAddress { get; set; } = null!;

    public string City { get; set; } = null!;

    public string State { get; set; } = null!;

    public string PostalCode { get; set; } = null!;

    // ISO 3166-1 alpha-2 (section 12). Le support fonctionnel reste Canada uniquement
    // (RegionCodeResolver.CountryCodeCanada) : ce champ documente l'intention sans
    // prétendre à une couverture internationale que le calcul de taxe/livraison ne sait
    // pas honorer.
    public string CountryCode { get; set; } = "CA";

    public bool IsDefaultShipping { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual AspNetUser ApplicationUser { get; set; } = null!;
}
