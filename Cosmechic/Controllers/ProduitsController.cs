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
        private const string ImagesSubfolder = "Images_Produits";

        private readonly CosmechicsContext _context;
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly IProductImageUploadService _imageUploadService;

        public ProduitsController(
            CosmechicsContext context,
            IWebHostEnvironment hostingEnvironment,
            IProductImageUploadService imageUploadService)
        {
            _context = context;
            _hostingEnvironment = hostingEnvironment;
            _imageUploadService = imageUploadService;
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
        // Route ID historique, conservée pour compatibilité (COSMECHIC-CATALOG-001,
        // section 18) : les liens déjà publiés continuent de fonctionner.
        [Authorize]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || !_context.Produits.Any())
            {
                return NotFound();
            }

            var produit = await _context.Produits
                .Include(p => p.Categorie)
                .Include(p => p.Brand)
                .Include(p => p.Images)
                .Include(p => p.Avis)
                .FirstOrDefaultAsync(m => m.ProduitId == id);
            if (produit == null)
            {
                return NotFound();
            }

            return View(produit);
        }

        // GET: /produits/{slug} — route canonique (COSMECHIC-CATALOG-001, section 18).
        [Authorize]
        public async Task<IActionResult> DetailsBySlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return NotFound();
            }

            var produit = await _context.Produits
                .Include(p => p.Categorie)
                .Include(p => p.Brand)
                .Include(p => p.Images)
                .Include(p => p.Avis)
                .FirstOrDefaultAsync(p => p.Slug == slug);
            if (produit == null)
            {
                return NotFound();
            }

            return View("Details", produit);
        }

        // GET: Produits/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create(int? Id)
        {
            var categories = _context.Categories.ToList();
            ViewData["CategorieId"] = Id;
            ViewBag.BrandId = new SelectList(_context.Brands.Where(b => b.Disponible).OrderBy(b => b.Nom), "BrandId", "Nom");
            return View();
        }

        // POST: Produits/Create
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("ProduitId,Nom,CategorieId,Description,Prix,Stock,Disponible,Image,Sku,Slug,BrandId,IngredientsInci,UsageInstructions,Warnings,NetQuantity,SeoTitle,SeoDescription")] Produit produit,
            IFormFile Image)
        {
            await ValidateCatalogFieldsAsync(produit, isNew: true);

            if (ModelState.IsValid)
            {
                if (Image != null)
                {
                    var uploadResult = await _imageUploadService.SaveAsync(Image, ImagesSubfolder);
                    if (!uploadResult.Succeeded)
                    {
                        ModelState.AddModelError(nameof(Image), DescribeUploadError(uploadResult.Outcome));
                        return await ReturnCreateViewWithLookups(produit);
                    }

                    produit.Image = uploadResult.StoredFileName!;
                    produit.Images.Add(new ProduitImage
                    {
                        FileName = uploadResult.StoredFileName!,
                        IsPrimary = true,
                        SortOrder = 0,
                        AltText = produit.Nom,
                    });
                }

                produit.DateCreation = DateTime.UtcNow;
                // Placeholder sans effet sur SQL Server : une colonne "rowversion" est
                // exclue de l'instruction INSERT par EF Core (valeur toujours calculée par
                // le moteur, jamais par le client) — nécessaire uniquement pour les
                // fournisseurs qui, contrairement à SQL Server, n'auto-génèrent pas cette
                // valeur (COSMECHIC-DATA-001/COSMECHIC-CATALOG-001).
                produit.RowVersion ??= new byte[8];
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

            return await ReturnCreateViewWithLookups(produit);
        }

        private async Task<IActionResult> ReturnCreateViewWithLookups(Produit produit)
        {
            var categories = _context.Categories.ToList();
            ViewBag.CategorieId = new SelectList(categories, "CategorieId", "Nom");
            ViewBag.BrandId = new SelectList(await _context.Brands.Where(b => b.Disponible).OrderBy(b => b.Nom).ToListAsync(), "BrandId", "Nom");
            return View(produit);
        }

        // COSMECHIC-CATALOG-001 (section 16/17) : validation serveur des champs catalogue
        // introduits ce lot. SKU requis pour tout nouveau produit (jamais généré
        // arbitrairement — l'admin doit fournir sa propre référence commerciale) ; Slug
        // auto-généré depuis le nom si l'admin le laisse vide, mais jamais régénéré une
        // fois existant (un renommage ne casse pas les liens publiés). Unicité vérifiée
        // explicitement (message clair) plutôt que de laisser remonter la violation de
        // contrainte SQL brute.
        private async Task ValidateCatalogFieldsAsync(Produit produit, bool isNew)
        {
            produit.Sku = produit.Sku?.Trim();
            if (string.IsNullOrEmpty(produit.Sku))
            {
                if (isNew)
                {
                    ModelState.AddModelError(nameof(Produit.Sku), "Le SKU est requis pour un nouveau produit.");
                }
            }
            else
            {
                var skuTaken = await _context.Produits.AnyAsync(p => p.Sku == produit.Sku && p.ProduitId != produit.ProduitId);
                if (skuTaken)
                {
                    ModelState.AddModelError(nameof(Produit.Sku), "Ce SKU est déjà utilisé par un autre produit.");
                }
            }

            if (string.IsNullOrWhiteSpace(produit.Slug))
            {
                var baseSlug = SlugGenerator.Slugify(produit.Nom ?? string.Empty);
                var candidate = baseSlug;
                var suffix = 2;
                while (await _context.Produits.AnyAsync(p => p.Slug == candidate && p.ProduitId != produit.ProduitId))
                {
                    candidate = $"{baseSlug}-{suffix}";
                    suffix++;
                }
                produit.Slug = candidate;
            }
            else
            {
                produit.Slug = SlugGenerator.Slugify(produit.Slug);
                var slugTaken = await _context.Produits.AnyAsync(p => p.Slug == produit.Slug && p.ProduitId != produit.ProduitId);
                if (slugTaken)
                {
                    ModelState.AddModelError(nameof(Produit.Slug), "Ce slug est déjà utilisé par un autre produit.");
                }
            }
        }

        // GET: Produits/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Produits == null)
            {
                return NotFound();
            }

            var produit = await _context.Produits.Include(p => p.Images).FirstOrDefaultAsync(p => p.ProduitId == id);
            if (produit == null)
            {
                return NotFound();
            }
            var categories = _context.Categories.ToList();
            ViewBag.CategorieId = new SelectList(categories, "CategorieId", "Nom");
            ViewBag.BrandId = new SelectList(_context.Brands.Where(b => b.Disponible).OrderBy(b => b.Nom), "BrandId", "Nom", produit.BrandId);

            return View(produit);
        }

        // POST: Produits/Edit/5
        // COSMECHIC-CATALOG-001 (section 36) : récupère l'entité existante puis n'applique
        // que les champs réellement modifiables par ce formulaire, au lieu d'un
        // _context.Update(produit) sur une instance reconstituée par le binder — ce dernier
        // aurait silencieusement écrasé DateCreation/RowVersion/NombreVentes (jamais liés)
        // à leur valeur par défaut à chaque modification (section 36 : "ne doit pas...
        // reset stock unexpectedly").
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind("ProduitId,Nom,CategorieId,Description,Prix,Stock,Disponible,Image,Sku,Slug,BrandId,IngredientsInci,UsageInstructions,Warnings,NetQuantity,SeoTitle,SeoDescription")] Produit posted,
            IFormFile Image)
        {
            if (id != posted.ProduitId)
            {
                return NotFound();
            }

            await ValidateCatalogFieldsAsync(posted, isNew: false);

            if (ModelState.IsValid)
            {
                var produit = await _context.Produits.FirstOrDefaultAsync(p => p.ProduitId == id);
                if (produit == null)
                {
                    return NotFound();
                }

                try
                {
                    if (Image != null)
                    {
                        var uploadResult = await _imageUploadService.SaveAsync(Image, ImagesSubfolder);
                        if (!uploadResult.Succeeded)
                        {
                            ModelState.AddModelError(nameof(Image), DescribeUploadError(uploadResult.Outcome));
                            return await ReturnEditViewWithLookups(posted);
                        }

                        posted.Image = uploadResult.StoredFileName!;
                        _context.ProduitImages.Add(new ProduitImage
                        {
                            ProduitId = produit.ProduitId,
                            FileName = uploadResult.StoredFileName!,
                            IsPrimary = true,
                            SortOrder = 0,
                            AltText = posted.Nom,
                        });
                        foreach (var existingImage in await _context.ProduitImages.Where(pi => pi.ProduitId == produit.ProduitId && pi.IsPrimary).ToListAsync())
                        {
                            existingImage.IsPrimary = false;
                        }
                    }

                    produit.Nom = posted.Nom;
                    produit.CategorieId = posted.CategorieId;
                    produit.Description = posted.Description;
                    produit.Prix = posted.Prix;
                    produit.Stock = posted.Stock;
                    produit.Disponible = posted.Disponible;
                    produit.Sku = posted.Sku;
                    produit.Slug = posted.Slug;
                    produit.BrandId = posted.BrandId;
                    produit.IngredientsInci = posted.IngredientsInci;
                    produit.UsageInstructions = posted.UsageInstructions;
                    produit.Warnings = posted.Warnings;
                    produit.NetQuantity = posted.NetQuantity;
                    produit.SeoTitle = posted.SeoTitle;
                    produit.SeoDescription = posted.SeoDescription;
                    if (Image != null)
                    {
                        produit.Image = posted.Image;
                    }
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
            return await ReturnEditViewWithLookups(posted);
        }

        private async Task<IActionResult> ReturnEditViewWithLookups(Produit produit)
        {
            var categories = _context.Categories.ToList();
            ViewBag.CategorieId = new SelectList(categories, "CategorieId", "Nom");
            ViewBag.BrandId = new SelectList(await _context.Brands.Where(b => b.Disponible).OrderBy(b => b.Nom).ToListAsync(), "BrandId", "Nom", produit.BrandId);
            return View(produit);
        }


        // GET: Produits/ManageImages/5 — galerie d'images multiples (COSMECHIC-CATALOG-001,
        // section 30). Réutilise IProductImageUploadService (COSMECHIC-SECURITY-002) : aucun
        // nouveau chemin d'upload non sécurisé.
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ManageImages(int id)
        {
            var produit = await _context.Produits.Include(p => p.Images).FirstOrDefaultAsync(p => p.ProduitId == id);
            if (produit == null)
            {
                return NotFound();
            }

            return View(produit);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddImage(int id, IFormFile file, string? altText)
        {
            var produit = await _context.Produits.FirstOrDefaultAsync(p => p.ProduitId == id);
            if (produit == null)
            {
                return NotFound();
            }

            if (file == null)
            {
                TempData["error"] = "Aucun fichier fourni.";
                return RedirectToAction(nameof(ManageImages), new { id });
            }

            var uploadResult = await _imageUploadService.SaveAsync(file, ImagesSubfolder);
            if (!uploadResult.Succeeded)
            {
                TempData["error"] = DescribeUploadError(uploadResult.Outcome);
                return RedirectToAction(nameof(ManageImages), new { id });
            }

            var hasPrimary = await _context.ProduitImages.AnyAsync(pi => pi.ProduitId == id && pi.IsPrimary);
            var maxSortOrder = await _context.ProduitImages.Where(pi => pi.ProduitId == id).Select(pi => (int?)pi.SortOrder).MaxAsync() ?? -1;

            _context.ProduitImages.Add(new ProduitImage
            {
                ProduitId = id,
                FileName = uploadResult.StoredFileName!,
                AltText = string.IsNullOrWhiteSpace(altText) ? produit.Nom : altText.Trim(),
                SortOrder = maxSortOrder + 1,
                // COSMECHIC-CATALOG-001 (section 32) : invariant 0-ou-1 image primaire —
                // la toute première image ajoutée devient automatiquement primaire.
                IsPrimary = !hasPrimary,
            });
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(ManageImages), new { id });
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPrimaryImage(int id, int imageId)
        {
            // COSMECHIC-CATALOG-001 (section 32) : jamais plus d'une image primaire par
            // produit — toutes les autres sont explicitement désactivées dans la même
            // opération.
            var images = await _context.ProduitImages.Where(pi => pi.ProduitId == id).ToListAsync();
            var target = images.FirstOrDefault(pi => pi.ProduitImageId == imageId);
            if (target == null)
            {
                return NotFound();
            }

            foreach (var image in images)
            {
                image.IsPrimary = image.ProduitImageId == imageId;
            }
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(ManageImages), new { id });
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteImage(int id, int imageId)
        {
            // COSMECHIC-CATALOG-001 (section 33) : suppression physique uniquement si
            // l'enregistrement appartient bien au produit de la route (ownership) et
            // uniquement le nom de fichier stocké en base (toujours un GUID généré
            // serveur, jamais une entrée client) — aucun chemin ne peut sortir du
            // répertoire géré.
            var image = await _context.ProduitImages.FirstOrDefaultAsync(pi => pi.ProduitImageId == imageId && pi.ProduitId == id);
            if (image == null)
            {
                return NotFound();
            }

            var filePath = Path.Combine(_hostingEnvironment.WebRootPath, ImagesSubfolder, image.FileName);
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

            var wasPrimary = image.IsPrimary;
            _context.ProduitImages.Remove(image);
            await _context.SaveChangesAsync();

            if (wasPrimary)
            {
                var next = await _context.ProduitImages.Where(pi => pi.ProduitId == id).OrderBy(pi => pi.SortOrder).FirstOrDefaultAsync();
                if (next != null)
                {
                    next.IsPrimary = true;
                    await _context.SaveChangesAsync();
                }
            }

            return RedirectToAction(nameof(ManageImages), new { id });
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
        // COSMECHIC-CATALOG-001 (section 37) : une suppression physique d'un produit
        // référencé par un historique de commande (OrderDetails) ou un avis (Avis) est
        // bloquée au niveau base (FK_OrderDetails_Produits/FK_Avis_Produits en
        // ClientSetNull sur colonnes non-nullables => NO ACTION réel côté SQL Server) —
        // avant, ceci provoquait un DbUpdateException non intercepté (crash 500). Un
        // produit avec historique est désormais désactivé (Disponible = false) plutôt que
        // supprimé ; seul un produit réellement sans historique est physiquement retiré
        // (avec ses images sur disque, la table ProduitImages étant en Cascade).
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.Produits == null)
            {
                return Problem("Entity set 'CosmechicContext.Produits'  is null.");
            }

            var produit = await _context.Produits.Include(p => p.Images).FirstOrDefaultAsync(p => p.ProduitId == id);
            if (produit == null)
            {
                return RedirectToAction(nameof(Index));
            }

            var hasHistory = await _context.OrderDetails.AnyAsync(od => od.ProduitId == id)
                || await _context.Avis.AnyAsync(a => a.ProduitId == id);

            if (hasHistory)
            {
                produit.Disponible = false;
                await _context.SaveChangesAsync();
                TempData["success"] = "Ce produit a un historique de commandes/avis : il a été désactivé plutôt que supprimé.";
                return RedirectToAction(nameof(Index), new { id = produit.CategorieId });
            }

            foreach (var image in produit.Images)
            {
                var filePath = Path.Combine(_hostingEnvironment.WebRootPath, ImagesSubfolder, image.FileName);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }

            _context.Produits.Remove(produit);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { id = produit.CategorieId });
        }

        private bool ProduitExists(int id)
        {
            return _context.Produits.Any(e => e.ProduitId == id);
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

        // COSMECHIC-CATALOG-001 (section 5/6) : corrige SEARCH-001 — l'ancienne
        // implémentation rendait "ResultatsRecherche" alors que cette vue n'existait pas
        // (crash systématique dès que la recherche ne tombait pas sur une correspondance
        // exacte unique). Redirection automatique supprimée : la recherche reste
        // prévisible (section 6), toujours une vraie page de résultats (0/1/N), jamais un
        // saut de fiche produit surprenant. GET uniquement : entièrement "bookmarkable"
        // (section 42).
        public async Task<IActionResult> Rechercher(CatalogSearchViewModel filters)
        {
            filters ??= new CatalogSearchViewModel();

            // Normalisation/bornage (section 9/13) : jamais d'exception ni de requête
            // dégénérée à cause d'une entrée client invalide.
            if (filters.Page < 1) filters.Page = 1;
            if (filters.PageSize < 1) filters.PageSize = CatalogSearchViewModel.DefaultPageSize;
            if (filters.PageSize > CatalogSearchViewModel.MaxPageSize) filters.PageSize = CatalogSearchViewModel.MaxPageSize;
            if (filters.MinPrice is < 0) filters.MinPrice = 0;
            if (filters.MaxPrice is < 0) filters.MaxPrice = 0;
            if (filters.MinPrice.HasValue && filters.MaxPrice.HasValue && filters.MinPrice > filters.MaxPrice)
            {
                (filters.MinPrice, filters.MaxPrice) = (filters.MaxPrice, filters.MinPrice);
            }

            IQueryable<Produit> query = _context.Produits
                .Include(p => p.Categorie)
                .Include(p => p.Brand)
                .Include(p => p.Images);

            var term = filters.Q?.Trim();
            if (!string.IsNullOrEmpty(term))
            {
                // COSMECHIC-CATALOG-001 (section 7) : insensible à la casse ET aux accents
                // via la collation SQL Server, pas de bricolage côté client — vérifié
                // empiriquement contre SQL Server réel (COLLATE Latin1_General_CI_AI).
                query = query.Where(p =>
                    EF.Functions.Collate(p.Nom, "Latin1_General_CI_AI").Contains(term) ||
                    (p.Description != null && EF.Functions.Collate(p.Description, "Latin1_General_CI_AI").Contains(term)) ||
                    EF.Functions.Collate(p.Categorie.Nom, "Latin1_General_CI_AI").Contains(term) ||
                    (p.Brand != null && EF.Functions.Collate(p.Brand.Nom, "Latin1_General_CI_AI").Contains(term)) ||
                    (p.Sku != null && EF.Functions.Collate(p.Sku, "Latin1_General_CI_AI").Contains(term)));
            }

            if (filters.CategoryId.HasValue)
            {
                query = query.Where(p => p.CategorieId == filters.CategoryId);
            }

            if (filters.BrandId.HasValue)
            {
                query = query.Where(p => p.BrandId == filters.BrandId);
            }

            if (filters.MinPrice.HasValue)
            {
                query = query.Where(p => p.Prix >= filters.MinPrice);
            }

            if (filters.MaxPrice.HasValue)
            {
                query = query.Where(p => p.Prix <= filters.MaxPrice);
            }

            if (filters.AvailableOnly)
            {
                // COSMECHIC-CATALOG-001 (section 23) : disponibilité client = publié
                // (Disponible) ET en stock — jamais l'un sans l'autre.
                query = query.Where(p => p.Disponible && p.Stock > 0);
            }

            query = filters.Sort switch
            {
                "price_asc" => query.OrderBy(p => p.Prix),
                "price_desc" => query.OrderByDescending(p => p.Prix),
                "name_asc" => query.OrderBy(p => p.Nom),
                "newest" => query.OrderByDescending(p => p.DateCreation),
                // "relevance" (défaut) : pas de moteur de scoring — un tri stable et
                // honnête (nom croissant) plutôt qu'une fausse pertinence.
                _ => query.OrderBy(p => p.Nom),
            };

            filters.TotalResults = await query.CountAsync();
            filters.TotalPages = filters.TotalResults == 0 ? 0 : (int)Math.Ceiling(filters.TotalResults / (double)filters.PageSize);
            if (filters.TotalPages > 0 && filters.Page > filters.TotalPages)
            {
                filters.Page = filters.TotalPages;
            }

            filters.Products = await query
                .Skip((filters.Page - 1) * filters.PageSize)
                .Take(filters.PageSize)
                .ToListAsync();

            filters.AvailableCategories = await _context.Categories
                .OrderBy(c => c.Nom)
                .Select(c => new SelectListItem(c.Nom, c.CategorieId.ToString()))
                .ToListAsync();

            filters.AvailableBrands = await _context.Brands
                .Where(b => b.Disponible)
                .OrderBy(b => b.Nom)
                .Select(b => new SelectListItem(b.Nom, b.BrandId.ToString()))
                .ToListAsync();

            return View("ResultatsRecherche", filters);
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
        [ValidateAntiForgeryToken]
        // COSMECHIC-SECURITY-002 (section 14) : n'accepte que ProduitId/Count depuis la
        // requête. Sans allowlist explicite, un client pourrait poster des champs
        // "Produit.*" (liaison de la propriété de navigation) que le binder tenterait de
        // matérialiser en entité Produit non suivie — ApplicationUserId est de toute façon
        // réaffecté ci-dessous depuis l'utilisateur authentifié, jamais depuis la requête.
        public IActionResult ItemDetails([Bind(nameof(ShoppingCart.ProduitId), nameof(ShoppingCart.Count))] ShoppingCart shoppingCart)
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
