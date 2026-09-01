using Cosmechic.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Cosmechic.ViewModels
{
    // COSMECHIC-CATALOG-001 (section 41) : état complet d'une recherche/filtre catalogue,
    // entièrement dérivable de la querystring (section 42 : bookmarkable, GET uniquement).
    public class CatalogSearchViewModel
    {
        public string? Q { get; set; }

        public int? CategoryId { get; set; }

        public int? BrandId { get; set; }

        public decimal? MinPrice { get; set; }

        public decimal? MaxPrice { get; set; }

        public bool AvailableOnly { get; set; }

        public string Sort { get; set; } = "relevance";

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = DefaultPageSize;

        public const int DefaultPageSize = 20;
        public const int MaxPageSize = 60;

        public int TotalResults { get; set; }

        public int TotalPages { get; set; }

        public List<Produit> Products { get; set; } = new();

        public List<SelectListItem> AvailableCategories { get; set; } = new();

        public List<SelectListItem> AvailableBrands { get; set; } = new();

        public bool HasAnyFilter =>
            !string.IsNullOrWhiteSpace(Q) || CategoryId.HasValue || BrandId.HasValue ||
            MinPrice.HasValue || MaxPrice.HasValue || AvailableOnly;
    }
}
