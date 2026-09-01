using Cosmechic.Models;
using Cosmechic.Models.ViewModels;
using Cosmechic.Services;
using Cosmechic.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace Cosmechic.Controllers
{
	public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly CosmechicsContext _context;
        private readonly IEmailSender _emailSender;
        private readonly BusinessInformationOptions _businessInformation;
        private readonly CommercePolicyOptions _commercePolicy;
        private readonly ApplicationOptions _applicationOptions;

        public HomeController(
            ILogger<HomeController> logger,
            CosmechicsContext context,
            IEmailSender emailSender,
            IOptions<BusinessInformationOptions> businessInformation,
            IOptions<CommercePolicyOptions> commercePolicy,
            IOptions<ApplicationOptions> applicationOptions)
        {
            _logger = logger;
            _context = context;
            _emailSender = emailSender;
            _businessInformation = businessInformation.Value;
            _commercePolicy = commercePolicy.Value;
            _applicationOptions = applicationOptions.Value;
        }

        // COSMECHIC-BUSINESS-POLICY-001 (section 9B) : sitemap.xml réellement exploitable,
        // maintenant que PRODUCTION_DOMAIN est approuvé. Liste volontairement restreinte
        // aux pages publiques statiques/institutionnelles + racines de catalogue — jamais
        // les routes privées/admin/webhook déjà listées dans robots.txt (voir
        // SitemapTests.cs). Un sitemap énumérant chaque produit/catégorie individuellement
        // est une extension future hors du périmètre approuvé de ce lot (voir
        // docs/audits/COSMECHIC-BUSINESS-POLICY-001.md). Si PublicBaseUrl n'est pas
        // configuré (développement/tests), retourne 404 plutôt que de fabriquer une URL
        // localhost/exemple dans un fichier destiné aux moteurs de recherche.
        // Route conventionnelle attendue par les moteurs de recherche (jamais /Home/Sitemap).
        [HttpGet("sitemap.xml")]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
        public IActionResult Sitemap()
        {
            if (string.IsNullOrWhiteSpace(_applicationOptions.PublicBaseUrl))
            {
                return NotFound();
            }

            var baseUrl = _applicationOptions.PublicBaseUrl.TrimEnd('/');
            var paths = new[]
            {
                string.Empty,
                "/Home/About",
                "/Home/Contact",
                "/Home/Faq",
                "/Home/Privacy",
                "/Home/Terms",
                "/Home/Shipping",
                "/Home/Returns",
                "/Produits/Index",
                "/Categories/Customer",
            };

            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            xml.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
            foreach (var path in paths)
            {
                xml.Append("<url><loc>").Append(WebUtility.HtmlEncode(baseUrl + path)).Append("</loc></url>");
            }
            xml.Append("</urlset>");

            return Content(xml.ToString(), "application/xml");
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
            ViewData["Title"] = "À propos";
            ViewData["MetaDescription"] = "Cosmechic est une boutique en ligne dédiée aux produits cosmétiques pour les personnes afro, noires et métissées.";
            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Title"] = "Nous joindre";
            ViewData["MetaDescription"] = "Contactez l'équipe Cosmechic pour toute question sur votre commande, un produit ou notre boutique.";
            return View(new ContactMessageInput());
        }

        // COSMECHIC-CONTENT-LEGAL-001 (section 9/25) : DTO étroit, antiforgery, rate
        // limiting dédié, réutilise IEmailSender (jamais de SmtpClient/MailKit direct
        // ici). Comportement déterministe si SMTP indisponible : l'exception de
        // SmtpEmailSender est interceptée, journalisée sans PII/secret, et l'utilisateur
        // reçoit un message d'erreur générique plutôt qu'une page 500.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("ContactForm")]
        public async Task<IActionResult> Contact(ContactMessageInput input)
        {
            ViewData["Title"] = "Nous joindre";
            ViewData["MetaDescription"] = "Contactez l'équipe Cosmechic pour toute question sur votre commande, un produit ou notre boutique.";

            if (!ModelState.IsValid)
            {
                return View(input);
            }

            if (string.IsNullOrWhiteSpace(_businessInformation.SupportEmail))
            {
                _logger.LogWarning("Formulaire Contact soumis mais aucune adresse de support n'est configurée.");
                ModelState.AddModelError(string.Empty, "Le formulaire de contact n'est pas disponible pour le moment. Veuillez réessayer plus tard.");
                return View(input);
            }

            var body = new StringBuilder()
                .Append("<p><strong>De :</strong> ").Append(WebUtility.HtmlEncode(input.Name))
                .Append(" (").Append(WebUtility.HtmlEncode(input.Email)).Append(")</p>")
                .Append("<p>").Append(WebUtility.HtmlEncode(input.Message).Replace("\n", "<br />")).Append("</p>")
                .ToString();

            try
            {
                await _emailSender.SendEmailAsync(_businessInformation.SupportEmail, $"Message du site Cosmechic de {input.Name}", body);
            }
            catch (Exception ex)
            {
                // Jamais de secret/PII au-delà de ce qui est déjà journalisé ailleurs
                // (aucun mot de passe/jeton dans ce chemin) — seule l'occurrence de
                // l'échec est notée.
                _logger.LogError(ex, "Échec de l'envoi du message du formulaire Contact.");
                ModelState.AddModelError(string.Empty, "Impossible d'envoyer votre message pour le moment. Veuillez réessayer plus tard.");
                return View(input);
            }

            TempData["ContactSuccess"] = true;
            return RedirectToAction(nameof(Contact));
        }

        public IActionResult Privacy()
        {
            ViewData["Title"] = "Politique de confidentialité";
            ViewData["MetaDescription"] = "Comment Cosmechic recueille, utilise et protège vos données personnelles.";
            ViewBag.SupportEmail = _businessInformation.SupportEmail;
            return View();
        }

        public IActionResult Terms()
        {
            ViewData["Title"] = "Conditions d'utilisation et de vente";
            ViewData["MetaDescription"] = "Conditions d'utilisation du site Cosmechic et conditions de vente applicables à vos commandes.";
            ViewBag.SupportEmail = _businessInformation.SupportEmail;
            return View();
        }

        public IActionResult Faq()
        {
            ViewData["Title"] = "Foire aux questions";
            ViewData["MetaDescription"] = "Réponses aux questions fréquentes sur le compte, les commandes, la livraison, les retours et les remboursements chez Cosmechic.";
            return View();
        }

        public IActionResult Shipping()
        {
            ViewData["Title"] = "Livraison";
            ViewData["MetaDescription"] = "Comment fonctionne la livraison chez Cosmechic : méthodes disponibles, coût calculé avant paiement.";
            ViewBag.ShippingMethods = _context.ShippingMethods.Where(m => m.IsActive).OrderBy(m => m.SortOrder).ToList();
            return View();
        }

        public IActionResult Returns()
        {
            ViewData["Title"] = "Retours et remboursements";
            ViewData["MetaDescription"] = "Comment demander un retour et suivre votre remboursement chez Cosmechic.";
            ViewBag.CommercePolicy = _commercePolicy;
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

		[HttpPost]
		[Authorize]
		[ValidateAntiForgeryToken]
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
