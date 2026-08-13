using Cosmechic.Models;
using Cosmechic.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Cosmechic.Controllers
{
	public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly CosmechicsContext _context;

        public HomeController(ILogger<HomeController> logger, CosmechicsContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var viewModel = new HomeIndexViewModel();

            // Récupération des catégories populaires
            var categoriesPopulaires = await _context.Categories
                .Include(c => c.Produits)
                .OrderByDescending(c => c.Produits.Count())
                .Take(4)
                .ToListAsync();

            viewModel.BestSellers = await _context.Produits
                .OrderByDescending(p => p.NombreVentes)
                .Take(4)
                .Select(p => new HomeProduit
                {
                    ProduitId = p.ProduitId,
                    Nom = p.Nom,
                    Description = p.Description,
                    Image = p.Image,
                    Prix = p.Prix
                })
                .ToListAsync();

            viewModel.Temoignages = await _context.TemoignagesClients
                .OrderByDescending(t => t.Note)
                .Take(3)
                .Select(t => new ClientTemoignage
                {
                    Nom = t.Nom,
                    Note = t.Note,
                    Date = t.Date,
                    Commentaire = t.Commentaire,
                    ProduitNom = t.Produit.Nom
                })
                .ToListAsync();

            // Récupération des promotions
            viewModel.Promotions = await _context.Promotions
                .Take(2)
                .Select(p => new ViewModels.Promotion
                {
                    Titre = p.Titre,
                    Description = p.Description,
                    Remise = p.Remise
                })
                .ToListAsync();

            // Récupération des articles de blog
            viewModel.ArticlesBlog = await _context.BlogPosts
                .Take(3)
                .Select(a => new ViewModels.BlogPost
                {
                    Titre = a.Titre,
                    Extrait = a.Contenu.Substring(0, Math.Min(100, a.Contenu.Length)) + "...",
                    Image = a.Image
                })
                .ToListAsync();

            ViewBag.Categories = await _context.Categories
                .OrderBy(c => c.Nom)
                .ToListAsync();

            return View(viewModel);
        }

        public IActionResult About()
        {
            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Terms()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

		[HttpPost]
		[Authorize]
		public IActionResult AjouterAuPanier(int produitId)
		{
			if (User?.Identity?.IsAuthenticated == true)
            {
				return Ok();
			}
			else
			{
				return Unauthorized();
			}
		}

	}
}
