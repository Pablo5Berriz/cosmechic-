using System;
using System.Collections.Generic;

namespace Cosmechic.Models;

public partial class Promotion
{
    public int Id { get; set; }

    public string Titre { get; set; } = null!;

    public string? Description { get; set; }

    public decimal Remise { get; set; }

    public DateTime DateDebut { get; set; }

    public DateTime DateFin { get; set; }
}
