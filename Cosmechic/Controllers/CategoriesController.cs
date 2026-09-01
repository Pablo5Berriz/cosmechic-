using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;


namespace Cosmechic.Controllers
{
    public class CategoriesController : Controller
    {
        private const string ImagesSubfolder = "Images Categories";

        private readonly CosmechicsContext _context;
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly IProductImageUploadService _imageUploadService;

        public CategoriesController(
            CosmechicsContext context,
            IWebHostEnvironment hostingEnvironment,
            IProductImageUploadService imageUploadService)
        {
            _context = context;
            _hostingEnvironment = hostingEnvironment;
            _imageUploadService = imageUploadService;
        }

        // GET: Categories
        // Vue de gestion administrative (liens Ajouter/Modifier/Supprimer) : réservée à
        // Admin. La navigation client passe par Customer(...) ci-dessous.
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(string sortOrder, string currentFilter, string searchString, int page = 1, int pageSize = 20)
        {
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CurrentFilter"] = searchString;

            var query = _context.Categories.AsQueryable();

            if (!String.IsNullOrEmpty(searchString))
            {
                query = query.Where(c => c.Nom.Contains(searchString));
            }

            switch (sortOrder)
            {
                case "name_desc":
                    query = query.OrderByDescending(c => c.Nom);
                    break;
                default:
                    query = query.OrderBy(c => c.Nom);
                    break;
            }

            var paginatedList = await PaginatedList<Category>.CreateAsync(query.AsNoTracking(), page, pageSize);
            return View(paginatedList);
        }

        // GET: /categories/{slug} — redirige vers la vitrine produit de la catégorie
        // (COSMECHIC-CATALOG-001, section 19). Pas de vue dédiée : une catégorie n'a pas
        // de contenu propre au-delà de la liste de ses produits, déjà servie par
        // ProduitsController.Customer.
        public async Task<IActionResult> CustomerBySlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return NotFound();
            }

            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Slug == slug);
            if (category == null)
            {
                return NotFound();
            }

            return RedirectToAction("Customer", "Produits", new { id = category.CategorieId });
        }

        // GET: Categories/Details/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.Categories == null)
            {
                return NotFound();
            }

            var category = await _context.Categories.FirstOrDefaultAsync(m => m.CategorieId == id);
            if (category == null)
            {
                return NotFound();
            }

            return View(category);
        }

        // GET: Categories/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            var categories = _context.Categories.ToList();
            var selectList = new SelectList(categories, "CategorieId", "Nom");
            var viewModel = new CategorieViewModel
            {
                Categories = selectList
            };

            return View(viewModel);
        }

        // POST: Categories/Create
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Nom,Description,Disponible,Image")] CategorieViewModel categoryViewModel)
        {
            if (ModelState.IsValid)
            {
                var category = new Category();
                category.Nom = categoryViewModel.Nom;
                category.Description = categoryViewModel.Description;
                category.Disponible = categoryViewModel.Disponible;
                if (categoryViewModel.Image != null)
                {
                    var uploadResult = await _imageUploadService.SaveAsync(categoryViewModel.Image, ImagesSubfolder);
                    if (!uploadResult.Succeeded)
                    {
                        ModelState.AddModelError(nameof(categoryViewModel.Image), DescribeUploadError(uploadResult.Outcome));
                        var categoriesForError = _context.Categories.ToList();
                        categoryViewModel.Categories = new SelectList(categoriesForError, "CategorieId", "Nom");
                        return View(categoryViewModel);
                    }

                    category.Image = uploadResult.StoredFileName!;
                }

                _context.Add(category);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            var categories = _context.Categories.ToList();
            var selectList = new SelectList(categories, "CategorieId", "Nom");
            var viewModel = new CategorieViewModel
            {
                Categories = selectList
            };
            return View(viewModel);
        }



        // GET: Categories/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Categories == null)
            {
                return NotFound();
            }

            var category = await _context.Categories.FindAsync(id);
            if (category == null)
            {
                return NotFound();
            }

            var categoryViewModel = new CategorieViewModel();
            categoryViewModel.CategorieId = category.CategorieId;
            categoryViewModel.Nom = category.Nom;
            categoryViewModel.Description = category.Description;
            categoryViewModel.Disponible = category.Disponible;

            var categories = _context.Categories.ToList();
            var selectList = new SelectList(categories, "CategorieId", "Nom");

            categoryViewModel.Categories = selectList;
            return View(categoryViewModel);
        }

        // POST: Categories/Edit/5
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("CategorieId,Nom,Description,Image,Disponible")] CategorieViewModel categoryViewModel)
        {
            if (id != categoryViewModel.CategorieId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var category = await _context.Categories.FindAsync(id);
                    category.Nom = categoryViewModel.Nom;
                    category.Description = categoryViewModel.Description;
                    category.Disponible = categoryViewModel.Disponible;
                    if (categoryViewModel.Image != null)
                    {
                        var uploadResult = await _imageUploadService.SaveAsync(categoryViewModel.Image, ImagesSubfolder);
                        if (!uploadResult.Succeeded)
                        {
                            ModelState.AddModelError(nameof(categoryViewModel.Image), DescribeUploadError(uploadResult.Outcome));
                            var categoriesForError = _context.Categories.ToList();
                            categoryViewModel.Categories = new SelectList(categoriesForError, "CategorieId", "Nom");
                            return View(categoryViewModel);
                        }

                        category.Image = uploadResult.StoredFileName!;
                    }

                    _context.Update(category);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CategoryExists(categoryViewModel.CategorieId))
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
            return View(categoryViewModel);
        }

        // GET: Categories/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || _context.Categories == null)
            {
                return NotFound();
            }

            var category = await _context.Categories.FirstOrDefaultAsync(m => m.CategorieId == id);
            if (category == null)
            {
                return NotFound();
            }

            return View(category);
        }

        // POST: Categories/Delete/5
        // COSMECHIC-CATALOG-001 (section 38) : une catégorie encore référencée par des
        // produits (FK_Produits_Categories en ClientSetNull sur colonne non-nullable =>
        // NO ACTION côté SQL Server) provoquait un DbUpdateException non intercepté
        // (crash 500). Bloqué explicitement avec un message clair plutôt que de laisser
        // remonter l'exception SQL brute — aucune suppression accidentelle des produits
        // qu'elle contient.
        [HttpPost, ActionName("Delete")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var category = await _context.Categories.FindAsync(id);
            if (category == null)
            {
                return RedirectToAction(nameof(Index));
            }

            var hasProducts = await _context.Produits.AnyAsync(p => p.CategorieId == id);
            if (hasProducts)
            {
                TempData["error"] = "Cette catégorie contient des produits et ne peut pas être supprimée. Déplacez ou supprimez d'abord ses produits.";
                return RedirectToAction(nameof(Index));
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        private bool CategoryExists(int id)
        {
            return _context.Categories.Any(e => e.CategorieId == id);
        }

        private static string DescribeUploadError(ImageUploadOutcome outcome) => outcome switch
        {
            ImageUploadOutcome.EmptyFile => "Le fichier image est vide.",
            ImageUploadOutcome.TooLarge => "Le fichier image dépasse la taille maximale autorisée.",
            ImageUploadOutcome.InvalidExtension => "Format de fichier non autorisé. Formats acceptés : .jpg, .jpeg, .png, .webp.",
            ImageUploadOutcome.InvalidContentType => "Le type de contenu du fichier ne correspond pas à son extension.",
            ImageUploadOutcome.InvalidSignature => "Le contenu du fichier ne correspond pas à une image valide.",
            _ => "Fichier image invalide.",
        };

        public async Task<IActionResult> Customer(string sortOrder, string currentFilter, string searchString, int page = 1, int pageSize = 20)
        {
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CurrentFilter"] = searchString;

            var query = _context.Categories.AsQueryable();

            if (!String.IsNullOrEmpty(searchString))
            {
                query = query.Where(c => c.Nom.Contains(searchString));
            }

            switch (sortOrder)
            {
                case "name_desc":
                    query = query.OrderByDescending(c => c.Nom);
                    break;
                default:
                    query = query.OrderBy(c => c.Nom);
                    break;
            }

            var paginatedList = await PaginatedList<Category>.CreateAsync(query.AsNoTracking(), page, pageSize);
            return View(paginatedList);
        }
    }
}
