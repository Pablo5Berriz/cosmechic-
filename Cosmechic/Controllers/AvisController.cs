using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cosmechic.Models;
using System.Security.Claims;

namespace Cosmechic.Controllers
{
	// Index/Details restent volontairement publics : les avis clients sont un contenu de
	// découverte produit destiné à être vus par tout visiteur (comme les témoignages en
	// page d'accueil). Ils n'exposent que Note/Commentaire/Date/UserName (vues Index et
	// Details vérifiées), aucune donnée personnelle sensible. Toute action qui modifie
	// l'état (Create/Edit/Delete) exige en revanche une authentification et un contrôle
	// de propriété — voir chaque action ci-dessous.
	public class AvisController : Controller
	{
		private readonly CosmechicsContext _context;

		public AvisController(CosmechicsContext context)
		{
			_context = context;
		}

		// GET: Avis
		public async Task<IActionResult> Index()
		{
			var cosmechicsContext = _context.Avis.Include(a => a.AspNetUser).Include(a => a.Produit);
			return View(await cosmechicsContext.ToListAsync());
		}

		// GET: Avis/Details/5
		public async Task<IActionResult> Details(int? id)
		{
			if (id == null)
			{
				return NotFound();
			}

			var avi = await _context.Avis
				.Include(a => a.AspNetUser)
				.Include(a => a.Produit)
				.FirstOrDefaultAsync(m => m.ReviewId == id);
			if (avi == null)
			{
				return NotFound();
			}

			return View(avi);
		}

        // GET: Avis/Create
        [Authorize]
        public async Task<IActionResult> Create()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var (userName, produitName) = await GetOrderDetails(userId);

            if (userName == null || produitName == null)
            {
                return RedirectToAction(nameof(Index));
            }

            ViewBag.UserName = userName;
            ViewBag.ProduitNom = produitName;
            return View(new Avi { DateReview = DateTime.Now.Date });
        }

        // POST: Avis/Create
        // L'identité de l'auteur vient toujours du serveur (jamais du client). La règle
        // métier déjà présente côté GET (on ne peut évaluer que ce qu'on a commandé) est
        // désormais également appliquée côté POST, pour ne pas dépendre du seul formulaire
        // affiché.
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ReviewId,AspNetUserId,ProduitId,Note,Commentaire,DateReview")] Avi avi)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            avi.AspNetUserId = userId;

            if (!await HasPurchasedAsync(userId, avi.ProduitId))
            {
                ModelState.AddModelError(string.Empty, "Vous ne pouvez évaluer que des produits que vous avez commandés.");
            }

            if (ModelState.IsValid)
            {
                _context.Add(avi);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(avi);
        }
        // GET: Avis/Edit/5
        // Un client ne peut modifier que son propre avis ; Admin peut modifier tout avis.
        [Authorize]
        public async Task<IActionResult> Edit(int? id)
		{
			if (id == null)
			{
				return NotFound();
			}

			var avi = await _context.Avis.FindAsync(id);
			if (avi == null)
			{
				return NotFound();
			}

			if (!IsOwnerOrAdmin(avi.AspNetUserId))
			{
				return Forbid();
			}

			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			var (userName, produits) = await GetOrderDetails(userId);

			ViewBag.UserName = userName;
			ViewBag.Produits = produits;

			return View(avi);
		}

		// POST: Avis/Edit/5
		[HttpPost]
		[Authorize]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Edit(int id, [Bind("ReviewId,AspNetUserId,ProduitId,Note,Commentaire,DateReview")] Avi avi)
		{
			if (id != avi.ReviewId)
			{
				return NotFound();
			}

			var existingAvi = await _context.Avis.AsNoTracking().FirstOrDefaultAsync(a => a.ReviewId == id);
			if (existingAvi == null)
			{
				return NotFound();
			}

			if (!IsOwnerOrAdmin(existingAvi.AspNetUserId))
			{
				return Forbid();
			}

			// L'auteur d'un avis n'est jamais réassignable via ce formulaire, même par son
			// propriétaire : on ignore la valeur postée et on conserve celle déjà en base.
			avi.AspNetUserId = existingAvi.AspNetUserId;

			if (ModelState.IsValid)
			{
				try
				{
					_context.Update(avi);
					await _context.SaveChangesAsync();
				}
				catch (DbUpdateConcurrencyException)
				{
					if (!AviExists(avi.ReviewId))
					{
						return NotFound();
					}
					else
					{
						throw;
					}
				}
				return RedirectToAction(nameof(Index));
			}
			return View(avi);
		}

		// GET: Avis/Delete/5
		// Un client ne peut supprimer que son propre avis ; Admin peut supprimer tout avis.
		[Authorize]
		public async Task<IActionResult> Delete(int? id)
		{
			if (id == null)
			{
				return NotFound();
			}

			var avi = await _context.Avis
				.Include(a => a.AspNetUser)
				.Include(a => a.Produit)
				.FirstOrDefaultAsync(m => m.ReviewId == id);
			if (avi == null)
			{
				return NotFound();
			}

			if (!IsOwnerOrAdmin(avi.AspNetUserId))
			{
				return Forbid();
			}

			return View(avi);
		}

		// POST: Avis/Delete/5
		[HttpPost, ActionName("Delete")]
		[Authorize]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteConfirmed(int id)
		{
			var avi = await _context.Avis.FindAsync(id);
			if (avi == null)
			{
				return NotFound();
			}

			if (!IsOwnerOrAdmin(avi.AspNetUserId))
			{
				return Forbid();
			}

			_context.Avis.Remove(avi);
			await _context.SaveChangesAsync();
			return RedirectToAction(nameof(Index));
		}

		private bool AviExists(int id)
		{
			return _context.Avis.Any(e => e.ReviewId == id);
		}

        private async Task<(string, string)> GetOrderDetails(string userId)
        {
            var userName = await _context.AspNetUsers.Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync();

            var produitName = await _context.OrderDetails
                                        .Include(od => od.Produit)
                                        .Where(od => od.OrderHeader.ApplicationUserId == userId)
                                        .Select(od => od.Produit.Nom)
                                        .FirstOrDefaultAsync();

            return (userName, produitName);
        }

        // Règle métier : on ne peut évaluer qu'un produit réellement commandé.
        private async Task<bool> HasPurchasedAsync(string? userId, int produitId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return false;
            }

            return await _context.OrderDetails
                .AnyAsync(od => od.ProduitId == produitId && od.OrderHeader.ApplicationUserId == userId);
        }

        // Niveau 2 d'autorisation (ownership) : un utilisateur ne peut modifier/supprimer
        // que son propre avis, sauf s'il est Admin.
        private bool IsOwnerOrAdmin(string resourceApplicationUserId)
        {
            if (User.IsInRole("Admin"))
            {
                return true;
            }

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return currentUserId != null && currentUserId == resourceApplicationUserId;
        }
    }
}
