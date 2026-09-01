using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Cosmechic.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Cosmechic.Controllers
{
    [Authorize]
    public class OrderHeadersController : Controller
    {
        private readonly CosmechicsContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public OrderHeadersController(CosmechicsContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: OrderHeaders
        public async Task<IActionResult> Index()
        {
            if (User.IsInRole("Admin"))
            {
                var allOrders = await _context.OrderHeaders.Include(o => o.ApplicationUser).ToListAsync();
                return View(allOrders);
            }
            else
            {
                var userId = _userManager.GetUserId(User);
                var userOrders = await _context.OrderHeaders
                    .Where(o => o.ApplicationUserId == userId)
                    .Include(o => o.ApplicationUser)
                    .ToListAsync();
                return View(userOrders);
            }
        }


        // GET: OrderHeaders/Details/5
        // Un client ne peut consulter que sa propre commande ; Admin peut tout consulter.
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.OrderHeaders == null)
            {
                return NotFound();
            }

            var orderHeader = await _context.OrderHeaders
                .Include(o => o.ApplicationUser)
                .Include(o => o.OrderDetails)
                .Include(o => o.ShippingMethod)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (orderHeader == null)
            {
                return NotFound();
            }

            if (!IsOwnerOrAdmin(orderHeader.ApplicationUserId))
            {
                return Forbid();
            }

            return View(orderHeader);
        }

        // GET/POST: OrderHeaders/Create
        // CRUD scaffold administratif : la création réelle d'une commande client passe par
        // CartController.SummaryPOST, jamais par ici. Aucune preuve qu'un client doive
        // pouvoir créer une commande arbitraire (avec ApplicationUserId, OrderStatus,
        // PaymentStatus... librement choisis) : réservé à Admin.
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            ViewData["ApplicationUserId"] = new SelectList(_context.AspNetUsers, "Id", "Id");
            return View();
        }

        // POST: OrderHeaders/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,PhoneNumber,StreetAddress,City,State,PostalCode,Name")] OrderHeader orderHeader)
        {
            if (ModelState.IsValid)
            {
                _context.Add(orderHeader);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["ApplicationUserId"] = new SelectList(_context.AspNetUsers, "Id", "Id", orderHeader.ApplicationUserId);
            return View(orderHeader);
        }

        // GET: OrderHeaders/Edit/5
        // CRUD scaffold administratif : un client ne doit pas pouvoir choisir librement
        // OrderStatus/PaymentStatus/OrderTotal/SessionId/PaymentIntentId/ApplicationUserId
        // de sa propre commande, encore moins de celle d'un autre. Réservé à Admin.
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.OrderHeaders == null)
            {
                return NotFound();
            }

            var orderHeader = await _context.OrderHeaders.FindAsync(id);
            if (orderHeader == null)
            {
                return NotFound();
            }
            ViewData["ApplicationUserId"] = new SelectList(_context.AspNetUsers, "Id", "Id", orderHeader.ApplicationUserId);
            return View(orderHeader);
        }

        // POST: OrderHeaders/Edit/5
        // COSMECHIC-COMMERCE-OPERATIONS-001B (audit section 5/8/54) : narrowé à l'adresse de
        // livraison/nom/téléphone uniquement — OrderStatus/PaymentStatus/FulfillmentStatus/
        // OrderTotal/SessionId/PaymentIntentId/ApplicationUserId ne sont plus jamais
        // modifiables ici (seule IOrderLifecycleService et les actions dédiées
        // d'OrderOperationsController peuvent faire transiter un statut ; OrderTotal et le
        // reste du snapshot financier sont immuables après création, section 80).
        //
        // _context.Update(orderHeader) sur l'entité liée (contenant désormais uniquement les
        // champs narrowés, le reste à leur valeur CLR par défaut) écraserait silencieusement
        // OrderStatus/OrderTotal/etc. avec null/0 — on charge donc l'entité existante et on
        // n'y reporte que les champs explicitement autorisés.
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,PhoneNumber,StreetAddress,City,State,PostalCode,Name")] OrderHeader posted)
        {
            if (id != posted.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var orderHeader = await _context.OrderHeaders.FindAsync(id);
                if (orderHeader == null)
                {
                    return NotFound();
                }

                orderHeader.Name = posted.Name;
                orderHeader.PhoneNumber = posted.PhoneNumber;
                orderHeader.StreetAddress = posted.StreetAddress;
                orderHeader.City = posted.City;
                orderHeader.State = posted.State;
                orderHeader.PostalCode = posted.PostalCode;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!OrderHeaderExists(id))
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
            ViewData["ApplicationUserId"] = new SelectList(_context.AspNetUsers, "Id", "Id", posted.ApplicationUserId);
            return View(posted);
        }

        // GET: OrderHeaders/Delete/5
        // CRUD scaffold administratif : réservé à Admin, aucun client ne doit pouvoir
        // supprimer une commande (la sienne ou celle d'un autre).
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || _context.OrderHeaders == null)
            {
                return NotFound();
            }

            var orderHeader = await _context.OrderHeaders
                .Include(o => o.ApplicationUser)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (orderHeader == null)
            {
                return NotFound();
            }

            return View(orderHeader);
        }

        // POST: OrderHeaders/Delete/5
        [HttpPost, ActionName("Delete")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.OrderHeaders == null)
            {
                return Problem("Entity set 'CosmechicsContext.OrderHeaders'  is null.");
            }
            var orderHeader = await _context.OrderHeaders.FindAsync(id);
            if (orderHeader != null)
            {
                _context.OrderHeaders.Remove(orderHeader);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool OrderHeaderExists(int id)
        {
            return (_context.OrderHeaders?.Any(e => e.Id == id)).GetValueOrDefault();
        }

        // Niveau 2 d'autorisation (ownership) : un utilisateur authentifié n'a le droit
        // d'agir sur une commande que s'il en est le propriétaire, ou s'il est Admin.
        private bool IsOwnerOrAdmin(string resourceApplicationUserId)
        {
            if (User.IsInRole("Admin"))
            {
                return true;
            }

            var currentUserId = _userManager.GetUserId(User);
            return currentUserId != null && currentUserId == resourceApplicationUserId;
        }
    }
}
