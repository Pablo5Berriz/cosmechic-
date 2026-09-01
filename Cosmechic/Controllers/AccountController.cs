using System.Security.Claims;
using Cosmechic.Models;
using Cosmechic.Models.ViewModels;
using Cosmechic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Controllers
{
    // COSMECHIC-ACCOUNT-001 : espace client authentifié — tableau de bord, profil,
    // adresses, historique de commandes, retours. Toute logique de cycle de vie
    // (annulation, retour, remboursement) est déléguée aux services existants
    // (ICancellationService via la vue OrderDetails/CartController.CancelOrder,
    // IReturnService/IOrderLifecycleService) — ce controller ne fait que lire et
    // présenter, jamais muter un statut lui-même (section 4/20/21).
    [Authorize]
    public class AccountController(
        CosmechicsContext context,
        UserManager<IdentityUser> userManager,
        IAddressService addressService) : Controller
    {
        private const int OrdersPageSize = 10;

        public async Task<IActionResult> Index()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var identityUser = await userManager.GetUserAsync(User);
            if (identityUser == null)
            {
                return NotFound();
            }

            var recentOrders = await context.OrderHeaders
                .Where(o => o.ApplicationUserId == userId)
                .OrderByDescending(o => o.OrderDate)
                .Take(5)
                .ToListAsync();

            var inProgressCount = await context.OrderHeaders
                .CountAsync(o => o.ApplicationUserId == userId
                    && o.OrderStatus != Utility.SD.OrderStatusCancelled
                    && o.FulfillmentStatus != Utility.SD.FulfillmentStatusDelivered);

            var addresses = await addressService.ListForUserAsync(userId);

            var vm = new AccountDashboardVM
            {
                Username = identityUser.UserName ?? string.Empty,
                Email = identityUser.Email ?? string.Empty,
                EmailConfirmed = identityUser.EmailConfirmed,
                RecentOrders = recentOrders,
                OrdersInProgressCount = inProgressCount,
                DefaultAddress = addresses.FirstOrDefault(a => a.IsDefaultShipping),
                AddressCount = addresses.Count,
            };

            return View(vm);
        }

        public async Task<IActionResult> Profile()
        {
            var identityUser = await userManager.GetUserAsync(User);
            if (identityUser == null)
            {
                return NotFound();
            }

            return View(BuildProfileVM(identityUser));
        }

        // COSMECHIC-ACCOUNT-001 (section 7/8/9) : seul PhoneNumber est modifiable ici, via
        // UserManager (même mécanisme que Identity/Manage/Index, jamais un second
        // système). Email/mot de passe/2FA restent gérés par les pages Identity
        // existantes, reliées depuis la vue — jamais réimplémentés ici.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(ProfileInput input)
        {
            var identityUser = await userManager.GetUserAsync(User);
            if (identityUser == null)
            {
                return NotFound();
            }

            var currentPhone = await userManager.GetPhoneNumberAsync(identityUser);
            if (input.PhoneNumber != currentPhone)
            {
                var result = await userManager.SetPhoneNumberAsync(identityUser, input.PhoneNumber);
                if (!result.Succeeded)
                {
                    ModelState.AddModelError(string.Empty, "Impossible de mettre à jour le numéro de téléphone.");
                    return View(BuildProfileVM(identityUser));
                }
            }

            TempData["success"] = "Profil mis à jour.";
            return RedirectToAction(nameof(Profile));
        }

        public async Task<IActionResult> Addresses()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var addresses = await addressService.ListForUserAsync(userId);
            return View(addresses);
        }

        public IActionResult CreateAddress() => View("AddressForm", new AddressFormInput());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAddress(AddressFormInput input)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var result = await addressService.CreateAsync(userId, ToAddressInput(input), input.SetAsDefault);
            if (result is AddressRejected rejected)
            {
                ModelState.AddModelError(string.Empty, rejected.Reason);
                return View("AddressForm", input);
            }

            TempData["success"] = "Adresse ajoutée.";
            return RedirectToAction(nameof(Addresses));
        }

        public async Task<IActionResult> EditAddress(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var address = await addressService.GetOwnedAsync(id, userId);
            if (address == null)
            {
                return NotFound();
            }

            return View("AddressForm", new AddressFormInput
            {
                Id = address.Id,
                Label = address.Label,
                RecipientName = address.RecipientName,
                PhoneNumber = address.PhoneNumber,
                StreetAddress = address.StreetAddress,
                City = address.City,
                State = address.State,
                PostalCode = address.PostalCode,
                CountryCode = address.CountryCode,
                SetAsDefault = address.IsDefaultShipping,
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAddress(int id, AddressFormInput input)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var result = await addressService.UpdateAsync(id, userId, ToAddressInput(input));
            if (result is AddressRejected rejected)
            {
                ModelState.AddModelError(string.Empty, rejected.Reason);
                input.Id = id;
                return View("AddressForm", input);
            }

            if (input.SetAsDefault)
            {
                await addressService.SetDefaultAsync(id, userId);
            }

            TempData["success"] = "Adresse mise à jour.";
            return RedirectToAction(nameof(Addresses));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAddress(AddressIdInput input)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var result = await addressService.DeleteAsync(input.Id, userId);
            TempData[result is AddressRejected rejected ? "error" : "success"] =
                result is AddressRejected r ? r.Reason : "Adresse supprimée.";
            return RedirectToAction(nameof(Addresses));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetDefaultAddress(AddressIdInput input)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var result = await addressService.SetDefaultAsync(input.Id, userId);
            TempData[result is AddressRejected rejected ? "error" : "success"] =
                result is AddressRejected r ? r.Reason : "Adresse par défaut mise à jour.";
            return RedirectToAction(nameof(Addresses));
        }

        // COSMECHIC-ACCOUNT-001 (section 16) : pagination obligatoire — jamais de
        // chargement illimité.
        public async Task<IActionResult> Orders(int page = 1)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            page = page < 1 ? 1 : page;

            var query = context.OrderHeaders
                .Where(o => o.ApplicationUserId == userId)
                .OrderByDescending(o => o.OrderDate);

            var totalCount = await query.CountAsync();
            var orders = await query
                .Skip((page - 1) * OrdersPageSize)
                .Take(OrdersPageSize)
                .ToListAsync();

            return View(new PagedOrdersVM
            {
                Orders = orders,
                Page = page,
                PageSize = OrdersPageSize,
                TotalCount = totalCount,
            });
        }

        // COSMECHIC-ACCOUNT-001 (section 17) : détail orienté client — snapshot produits/
        // adresse, statuts, tracking, retours et remboursements liés à CETTE commande.
        // Aucune information administrative interne (AdminComment, IdempotencyKey,
        // StripeRefundId...) n'est exposée par la vue associée.
        public async Task<IActionResult> OrderDetails(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var order = await context.OrderHeaders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Produit)
                .Include(o => o.ShippingMethod)
                .Include(o => o.ReturnRequests).ThenInclude(r => r.Items)
                .Include(o => o.Refunds)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return NotFound();
            }

            if (order.ApplicationUserId != userId)
            {
                return Forbid();
            }

            return View(order);
        }

        public async Task<IActionResult> Returns()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var returns = await context.ReturnRequests
                .Where(r => r.ApplicationUserId == userId)
                .Include(r => r.Items).ThenInclude(i => i.OrderDetail)
                .Include(r => r.Refunds)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return View(returns);
        }

        private static AddressInput ToAddressInput(AddressFormInput input) => new(
            input.Label, input.RecipientName, input.PhoneNumber, input.StreetAddress,
            input.City, input.State, input.PostalCode, input.CountryCode);

        private ProfileVM BuildProfileVM(IdentityUser user) => new()
        {
            Username = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumber = user.PhoneNumber,
        };

        private string? GetCurrentUserId() => (User.Identity as ClaimsIdentity)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
