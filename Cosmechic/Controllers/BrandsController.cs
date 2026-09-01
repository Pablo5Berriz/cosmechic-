using Cosmechic.Models;
using Cosmechic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Controllers
{
    // COSMECHIC-CATALOG-001 (section 39) : administration minimale de la marque —
    // liste/création/édition/désactivation. Jamais de suppression physique (voir
    // CosmechicsContext : FK_Produits_Brands en Restrict) : une marque référencée par des
    // produits ne doit jamais disparaître silencieusement de leur navigation.
    [Authorize(Roles = "Admin")]
    public class BrandsController : Controller
    {
        private readonly CosmechicsContext _context;

        public BrandsController(CosmechicsContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            return View(await _context.Brands.OrderBy(b => b.Nom).ToListAsync());
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Nom")] Brand brand)
        {
            brand.Nom = brand.Nom?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(brand.Nom))
            {
                ModelState.AddModelError(nameof(Brand.Nom), "Le nom de la marque est requis.");
            }
            else if (await _context.Brands.AnyAsync(b => b.Nom == brand.Nom))
            {
                ModelState.AddModelError(nameof(Brand.Nom), "Cette marque existe déjà.");
            }

            if (ModelState.IsValid)
            {
                var baseSlug = SlugGenerator.Slugify(brand.Nom);
                var candidate = baseSlug;
                var suffix = 2;
                while (await _context.Brands.AnyAsync(b => b.Slug == candidate))
                {
                    candidate = $"{baseSlug}-{suffix}";
                    suffix++;
                }
                brand.Slug = candidate;
                brand.Disponible = true;

                _context.Add(brand);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(brand);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var brand = await _context.Brands.FindAsync(id);
            if (brand == null)
            {
                return NotFound();
            }

            return View(brand);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("BrandId,Nom,Disponible")] Brand posted)
        {
            if (id != posted.BrandId)
            {
                return NotFound();
            }

            posted.Nom = posted.Nom?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(posted.Nom))
            {
                ModelState.AddModelError(nameof(Brand.Nom), "Le nom de la marque est requis.");
            }
            else if (await _context.Brands.AnyAsync(b => b.Nom == posted.Nom && b.BrandId != id))
            {
                ModelState.AddModelError(nameof(Brand.Nom), "Cette marque existe déjà.");
            }

            if (ModelState.IsValid)
            {
                var brand = await _context.Brands.FindAsync(id);
                if (brand == null)
                {
                    return NotFound();
                }

                brand.Nom = posted.Nom;
                brand.Disponible = posted.Disponible;
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(posted);
        }
    }
}
