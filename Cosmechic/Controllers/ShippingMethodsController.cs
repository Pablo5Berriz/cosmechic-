using Cosmechic.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Controllers
{
    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 22) : administration minimale — liste,
    // création, édition, désactivation. Jamais de suppression physique : une méthode déjà
    // référencée par des commandes historiques (FK_OrderHeaders_ShippingMethods en Restrict)
    // ne doit jamais disparaître silencieusement de leur navigation ; IsActive=false suffit
    // à la retirer du choix client (ShippingCalculator la rejette déjà).
    [Authorize(Roles = "Admin")]
    public class ShippingMethodsController : Controller
    {
        private readonly CosmechicsContext _context;

        public ShippingMethodsController(CosmechicsContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            return View(await _context.ShippingMethods.OrderBy(m => m.SortOrder).ToListAsync());
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Description,Price,FreeShippingThreshold,EstimatedMinDays,EstimatedMaxDays,SortOrder")] ShippingMethod shippingMethod)
        {
            shippingMethod.Name = shippingMethod.Name?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(shippingMethod.Name))
            {
                ModelState.AddModelError(nameof(ShippingMethod.Name), "Le nom est requis.");
            }

            if (shippingMethod.Price < 0)
            {
                ModelState.AddModelError(nameof(ShippingMethod.Price), "Le prix ne peut pas être négatif.");
            }

            if (shippingMethod.FreeShippingThreshold.HasValue && shippingMethod.FreeShippingThreshold.Value < 0)
            {
                ModelState.AddModelError(nameof(ShippingMethod.FreeShippingThreshold), "Le seuil ne peut pas être négatif.");
            }

            if (ModelState.IsValid)
            {
                shippingMethod.IsActive = true;
                _context.Add(shippingMethod);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(shippingMethod);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var shippingMethod = await _context.ShippingMethods.FindAsync(id);
            if (shippingMethod == null)
            {
                return NotFound();
            }

            return View(shippingMethod);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ShippingMethodId,Name,Description,Price,FreeShippingThreshold,IsActive,EstimatedMinDays,EstimatedMaxDays,SortOrder")] ShippingMethod posted)
        {
            if (id != posted.ShippingMethodId)
            {
                return NotFound();
            }

            posted.Name = posted.Name?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(posted.Name))
            {
                ModelState.AddModelError(nameof(ShippingMethod.Name), "Le nom est requis.");
            }

            if (posted.Price < 0)
            {
                ModelState.AddModelError(nameof(ShippingMethod.Price), "Le prix ne peut pas être négatif.");
            }

            if (posted.FreeShippingThreshold.HasValue && posted.FreeShippingThreshold.Value < 0)
            {
                ModelState.AddModelError(nameof(ShippingMethod.FreeShippingThreshold), "Le seuil ne peut pas être négatif.");
            }

            if (ModelState.IsValid)
            {
                var shippingMethod = await _context.ShippingMethods.FindAsync(id);
                if (shippingMethod == null)
                {
                    return NotFound();
                }

                shippingMethod.Name = posted.Name;
                shippingMethod.Description = posted.Description;
                shippingMethod.Price = posted.Price;
                shippingMethod.FreeShippingThreshold = posted.FreeShippingThreshold;
                shippingMethod.IsActive = posted.IsActive;
                shippingMethod.EstimatedMinDays = posted.EstimatedMinDays;
                shippingMethod.EstimatedMaxDays = posted.EstimatedMaxDays;
                shippingMethod.SortOrder = posted.SortOrder;

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(posted);
        }
    }
}
