using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.ViewModels;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Cosmechic.Controllers
{
    public class ProduitsController : Controller
    {
        private readonly CosmechicsContext _context;
        private readonly IWebHostEnvironment _hostingEnvironment;

        public ProduitsController(CosmechicsContext context, IWebHostEnvironment hostingEnvironment)
        {
            _context = context;
            _hostingEnvironment = hostingEnvironment;
        }

        // GET: Produits
        public async Task<IActionResult> Index(int? id = 1, int page = 1, int pageSize = 20)
        {
            var categorie = await _context.Categories.Include(c => c.Produits).FirstOrDefaultAsync(c => c.CategorieId == id);

            if (categorie == null)
            {
                return NotFound("Catégorie non trouvée.");
            }

            var produitsQuery = _context.Produits.Where(p => p.CategorieId == id);

            var paginatedProduits = await PaginatedList<Produit>.CreateAsync(produitsQuery, page, pageSize);

            ViewBag.CategorieNom = categorie.Nom;
            ViewBag.CategorieId = id;

            return View(paginatedProduits);
        }


        // GET: Produits/Details/5
        [Authorize]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || !_context.Produits.Any())
            {
                return NotFound();
            }

            var produit = await _context.Produits
                .Include(p => p.Categorie)
                .FirstOrDefaultAsync(m => m.ProduitId == id);
            if (produit == null)
            {
                return NotFound();
            }

            return View(produit);
        }

        // GET: Produits/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create(int? Id)
        {
            var categories = _context.Categories.ToList();
            ViewData["CategorieId"] = Id;
            return View();
        }

        // POST: Produits/Create
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ProduitId,Nom,CategorieId,Description,Prix,Stock,Disponible,Image")] Produit produit, IFormFile Image)
        {
            if (ModelState.IsValid)
            {
                if (Image != null)
                {
                    string uploadsFolder = Path.Combine(_hostingEnvironment.WebRootPath, "Images_Produits");
                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + Image.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await Image.CopyToAsync(fileStream);
                    }

                    produit.Image = uniqueFileName;
                }
                _context.Add(produit);
                await _context.SaveChangesAsync();
                return RedirectToAction("Index", "Produits", new { id = produit.CategorieId });
            }
            else
            {
                foreach (var modelState in ViewData.ModelState.Values)
                {
                    foreach (var error in modelState.Errors)
                    {
                        ModelState.AddModelError("", error.ErrorMessage);
                    }
                }
            }
            var categories = _context.Categories.ToList();
            ViewBag.CategorieId = new SelectList(categories, "CategorieId", "Nom");

            return View(produit);
        }

        // GET: Produits/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Produits == null)
            {
                return NotFound();
            }

            var produit = await _context.Produits.FindAsync(id);
            if (produit == null)
            {
                return NotFound();
            }
            var categories = _context.Categories.ToList();
            ViewBag.CategorieId = new SelectList(categories, "CategorieId", "Nom");

            return View(produit);
        }

        // POST: Produits/Edit/5
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ProduitId,Nom,CategorieId,Description,Prix,Stock,Disponible,Image")] Produit produit, IFormFile Image)
        {
            if (id != produit.ProduitId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    if (Image != null)
                    {
                        string uploadsFolder = Path.Combine(_hostingEnvironment.WebRootPath, "Images_Produits");
                        string uniqueFileName = Guid.NewGuid().ToString() + "_" + Image.FileName;
                        string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await Image.CopyToAsync(fileStream);
                        }

                        produit.Image = uniqueFileName;
                    }
                    _context.Update(produit);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ProduitExists(produit.ProduitId))
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
            var categories = _context.Categories.ToList();
            ViewBag.CategorieId = new SelectList(categories, "CategorieId", "Nom");
            return View(produit);
        }


        // GET: Produits/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || !_context.Produits.Any())
            {
                return NotFound();
            }

            var produit = await _context.Produits
                .Include(p => p.Categorie)
                .FirstOrDefaultAsync(m => m.ProduitId == id);
            if (produit == null)
            {
                return NotFound();
            }

            return View(produit);
        }

        // POST: Produits/Delete/5
        [HttpPost, ActionName("Delete")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.Produits == null)
            {
                return Problem("Entity set 'CosmechicContext.Produits'  is null.");
            }
            var produit = await _context.Produits.FindAsync(id);
            if (produit != null)
            {
                _context.Produits.Remove(produit);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        private bool ProduitExists(int id)
        {
            return _context.Produits.Any(e => e.ProduitId == id);
        }

        public async Task<IActionResult> Rechercher(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return View("Index");
            }

            var produit = await _context.Produits
                                        .FirstOrDefaultAsync(p => p.Nom.Contains(query));
            if (produit != null)
            {
                return RedirectToAction("Details", new { id = produit.ProduitId });
            }
            else
            {
                var produits = await _context.Produits
                                             .Where(p => p.Nom.Contains(query))
                                             .ToListAsync();
                return View("ResultatsRecherche", produits);
            }
        }

        public IActionResult ItemDetails(int productId)
        {
            var product = _context.Produits
                                  .Include(p => p.Categorie) 
                                  .FirstOrDefault(p => p.ProduitId == productId);

            if (product == null)
            {
                return NotFound(); 
            }

            ShoppingCart cart = new ShoppingCart
            {
                Produit = product,
                Count = 1,
                ProduitId = productId
            };

            return View(cart);
        }

        // COSMECHIC-ECOM-CORE-001 (section 5) : l'ajout au panier ne décrémente plus
        // jamais Produit.Stock. Le stock n'est consommé qu'au fulfillment réel d'une
        // commande payée (StripeFulfillmentService), pas à l'ajout au panier — un panier
        // abandonné ou vidé ne doit laisser aucune trace sur le stock. Cette action se
        // limite à valider la quantité demandée (section 6) et à créer/mettre à jour la
        // ligne de panier ; la disponibilité du stock n'est vérifiée qu'à titre informatif
        // pour l'utilisateur, jamais comme une réservation.
        [HttpPost]
        [Authorize]
        public IActionResult ItemDetails(ShoppingCart shoppingCart)
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;
            shoppingCart.ApplicationUserId = userId;

            if (!CartQuantityPolicy.IsValidRequestedQuantity(shoppingCart.Count))
            {
                TempData["error"] = "Quantité invalide";
                return RedirectToAction(nameof(Index));
            }

            var product = _context.Produits.Find(shoppingCart.ProduitId);
            if (product == null)
            {
                TempData["error"] = "Produit introuvable";
                return RedirectToAction(nameof(Index));
            }

            ShoppingCart cartFromDb = _context.ShoppingCarts.Where(u => u.ApplicationUserId == userId && u.ProduitId == shoppingCart.ProduitId).FirstOrDefault();
            var resultingQuantity = (cartFromDb?.Count ?? 0) + shoppingCart.Count;

            // Vérification purement informative (pas une réservation) : le stock réel n'est
            // vérifié et consommé qu'au moment du fulfillment de la commande payée.
            if (product.Stock < resultingQuantity)
            {
                TempData["error"] = "Quantité non disponible";
                return RedirectToAction(nameof(Index));
            }

            if (cartFromDb != null)
            {
                cartFromDb.Count = resultingQuantity;
                _context.ShoppingCarts.Update(cartFromDb);
            }
            else
            {
                _context.ShoppingCarts.Add(shoppingCart);
            }

            _context.SaveChanges();

            TempData["success"] = "Produit ajouté au panier avec succès";
            return RedirectToAction(nameof(Customer));
        }


        public async Task<IActionResult> Customer(int? id = 1, int page = 1, int pageSize = 20)
        {
            var categorie = await _context.Categories.Include(c => c.Produits).FirstOrDefaultAsync(c => c.CategorieId == id);

            if (categorie == null)
            {
                return NotFound("Catégorie non trouvée.");
            }

            var produitsQuery = _context.Produits.Where(p => p.CategorieId == id);

            var paginatedProduits = await PaginatedList<Produit>.CreateAsync(produitsQuery, page, pageSize);

            ViewBag.CategorieNom = categorie.Nom;
            ViewBag.CategorieId = id;

            return View(paginatedProduits);
        }

        public async Task<IActionResult> ParCategorie(int id)
        {
            var categorie = await _context.Categories.FindAsync(id);
            if (categorie == null)
            {
                return NotFound();
            }

            var produits = await _context.Produits
                .Where(p => p.CategorieId == id)
                .ToListAsync();

            ViewBag.CategorieNom = categorie.Nom;

            return View(produits);
        }
    }
}
