using System.Security.Claims;
using Cosmechic.Models;
using Cosmechic.Models.ViewModels;
using Cosmechic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Controllers
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 51/55) : surface client minimale — demander
    // un retour, consulter le statut d'un retour déjà demandé. Rien d'autre (pas de tableau
    // de bord complet, réservé à COSMECHIC-ACCOUNT-001).
    [Authorize]
    public class ReturnsController(CosmechicsContext context, IReturnService returnService) : Controller
    {
        // Renommée en C# pour ne pas masquer ControllerBase.Request (CS0108) ; l'URL/nom de
        // route reste "Request" via [ActionName] — aucune vue ni aucun test n'a besoin de
        // changer.
        [ActionName("Request")]
        public async Task<IActionResult> RequestReturn(int orderId)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var order = await context.OrderHeaders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
            {
                return NotFound();
            }

            // Ownership (section 18/55) : jamais la commande d'un autre client.
            if (order.ApplicationUserId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var vm = new ReturnRequestFormVM { OrderId = order.Id };

            foreach (var detail in order.OrderDetails)
            {
                var eligibility = await returnService.CanRequestReturnAsync(detail, 1);
                var alreadyClaimed = await context.ReturnItems
                    .Where(ri => ri.OrderDetailId == detail.Id && ri.ReturnRequest.Status != Utility.SD.ReturnStatusRejected)
                    .SumAsync(ri => (int?)ri.Quantity) ?? 0;

                var maxReturnable = eligibility is ReturnEligible ? detail.Count - alreadyClaimed : 0;

                vm.Lines.Add(new ReturnableLineVM
                {
                    OrderDetailId = detail.Id,
                    ProduitNom = detail.ProduitNom ?? $"Produit #{detail.ProduitId}",
                    Purchased = detail.Count,
                    MaxReturnable = maxReturnable,
                });
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("Request")]
        public async Task<IActionResult> RequestReturnSubmit(CreateReturnRequestInput input)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var items = input.Items
                .Where(i => i.Quantity > 0)
                .Select(i => new ReturnItemInput(i.OrderDetailId, i.Quantity, i.Reason))
                .ToList();

            // userId dérivé de l'identité authentifiée (jamais du formulaire, section 54) —
            // le service revérifie lui-même l'ownership de la commande.
            var result = await returnService.CreateReturnRequestAsync(input.OrderId, userId, input.Reason, input.CustomerComment, items);

            if (result is ReturnRequestRejectedByPolicy rejected)
            {
                TempData["error"] = rejected.Reason;
                return RedirectToAction("Request", new { orderId = input.OrderId });
            }

            var created = (ReturnRequestCreated)result;
            return RedirectToAction(nameof(Details), new { id = created.ReturnRequestId });
        }

        public async Task<IActionResult> Details(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var returnRequest = await context.ReturnRequests
                .Include(rr => rr.Items).ThenInclude(ri => ri.OrderDetail)
                .Include(rr => rr.Refunds)
                .FirstOrDefaultAsync(rr => rr.Id == id);

            if (returnRequest == null)
            {
                return NotFound();
            }

            if (returnRequest.ApplicationUserId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            return View(returnRequest);
        }

        private string? GetCurrentUserId() => (User.Identity as ClaimsIdentity)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
