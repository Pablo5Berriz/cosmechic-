using System;
using System.Collections.Generic;

namespace Cosmechic.Models;

public class TemoignagesClient
{
    public int Id { get; set; }
    public string? Nom { get; set; }
    public string? Commentaire { get; set; }
    public int Note { get; set; }
    public DateTime Date { get; set; }

   
    public int ProduitId { get; set; }
    public virtual Produit Produit { get; set; } 
}
