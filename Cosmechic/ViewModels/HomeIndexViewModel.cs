namespace Cosmechic.ViewModels
{
    public class HomeIndexViewModel
    {
        public List<HomeProduit> BestSellers { get; set; } 
        public List<ClientTemoignage> Temoignages { get; set; } 
        public List<Promotion> Promotions { get; set; } 
        public List<BlogPost> ArticlesBlog { get; set; } 
    }

    public class HomeProduit
    {
        public int ProduitId { get; set; }
        public string? Nom { get; set; }
        public string? Description { get; set; }
        public string? Image { get; set; }
        public decimal Prix { get; set; }
    }

    public class ClientTemoignage
    {
        public string? Nom { get; set; }
        public string? Commentaire { get; set; }
        public int Note { get; set; }
        public DateTime Date { get; set; }
        public string? ProduitNom { get; set; }
    }

    public class Promotion
    {
        public string? Titre { get; set; }
        public string? Description { get; set; }
        public decimal Remise { get; set; }
    }

    public class BlogPost
    {
        public string? Titre { get; set; }
        public string? Extrait { get; set; }
        public string? Image { get; set; }
    }
}