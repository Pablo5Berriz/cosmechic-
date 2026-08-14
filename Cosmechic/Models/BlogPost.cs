using System;
using System.Collections.Generic;

namespace Cosmechic.Models;

public partial class BlogPost
{
    public int Id { get; set; }

    public string Titre { get; set; } = null!;

    public string Contenu { get; set; } = null!;

    public string? Image { get; set; }

    public DateTime? DatePublication { get; set; }
}
