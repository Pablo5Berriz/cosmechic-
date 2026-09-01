using Cosmechic.Models.ViewModels;
using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Controllers

{

	[Authorize]

	public class CartController(CosmechicsContext context, ICheckoutService checkoutService) : Controller
	{

		private CosmechicsContext _context = context;
		private readonly ICheckoutService _checkoutService = checkoutService;

		[BindProperty]

		public required ShoppingCartVM ShoppingCartVM { get; set; }

        public IActionResult Index()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            ShoppingCartVM = new()
            {
                ShoppingCartList = _context.ShoppingCarts
                    .Where(u => u.ApplicationUserId == userId)
                    .Include(x => x.Produit), 
                OrderHeader = new()
            };

            foreach (var cart in ShoppingCartVM.ShoppingCartList)
            {
                ShoppingCartVM.OrderHeader.OrderTotal += ((decimal)cart.Prix * cart.Count); 
            }

            return View(ShoppingCartVM);
        }

        // COSMECHIC-COMMERCE-OPERATIONS-001A (section 44) : construit un aperçu (sous-total,
        // méthodes de livraison actives, taux de taxe actifs pour un calcul d'aperçu côté
        // client) — jamais utilisé comme source de vérité ; SummaryPOST recalcule tout depuis
        // la base via CheckoutService, indépendamment de ce qui est affiché ici.
        public IActionResult Summary()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var applicationUser = _context.AspNetUsers.FirstOrDefault(u => u.Id == userId);

            var summaryVM = new CheckoutSummaryVM
            {
                ShoppingCartList = _context.ShoppingCarts
                    .Where(u => u.ApplicationUserId == userId)
                    .Include(x => x.Produit)
                    .ToList(),
                ShippingMethods = _context.ShippingMethods
                    .Where(m => m.IsActive)
                    .OrderBy(m => m.SortOrder)
                    .ToList(),
                ActiveTaxRates = _context.TaxRates
                    .Where(r => r.IsActive && r.CountryCode == RegionCodeResolver.CountryCodeCanada)
                    .ToList(),
                Input = new CheckoutFormInput
                {
                    Name = applicationUser?.UserName,
                    PhoneNumber = applicationUser?.PhoneNumber,
                },
            };

            summaryVM.Subtotal = summaryVM.ShoppingCartList.Sum(item => item.Produit.Prix * item.Count);

            return View(summaryVM);
        }

        [HttpPost]
		[ValidateAntiForgeryToken]
		[ActionName("Summary")]

		// COSMECHIC-ECOM-CORE-001 (sections 8, 28) / COSMECHIC-COMMERCE-OPERATIONS-001A
		// (section 41/44) : cette action ne fait plus aucun calcul financier ni aucun appel
		// Stripe elle-meme - le paramètre lié n'expose que les champs de livraison
		// légitimement modifiables par le client (adresse + méthode de livraison choisie ;
		// aucune propriété OrderTotal/Subtotal/ShippingAmount/TaxAmount/PaymentStatus/
		// OrderStatus/SessionId/PaymentIntentId/ApplicationUserId n'existe sur ce type) et
		// délègue tout le reste à CheckoutService, seule source de vérité pour la création de
		// commande.
		public async Task<IActionResult> SummaryPOST(CheckoutFormInput input)

		{

			var userId = GetCurrentUserId();
			if (userId == null)
			{
				return Unauthorized();
			}

			var shipping = new ShippingAddress(
				input?.Name ?? string.Empty,
				input?.PhoneNumber ?? string.Empty,
				input?.StreetAddress ?? string.Empty,
				input?.City ?? string.Empty,
				input?.State ?? string.Empty,
				input?.PostalCode ?? string.Empty,
				input?.ShippingMethodId ?? 0);

			var domain = Request.Scheme + "://" + Request.Host.Value + "/";

			var result = await _checkoutService.CreateCheckoutSessionAsync(userId, shipping, domain);

			if (result is CheckoutFailed failed)
			{
				TempData["error"] = failed.Reason;
				return RedirectToAction(nameof(Index));
			}

			var created = (CheckoutSessionCreated)result;
			Response.Headers.Add("Location", created.RedirectUrl);
			return new StatusCodeResult(303);

		}


		// COSMECHIC-ECOM-CORE-001 (section 11) : cette action est desormais une vue d'etat
		// pure. Elle lit la commande, verifie l'ownership (controle SECURITY-001 conserve
		// integralement), et affiche l'etat courant tel qu'etabli par le webhook Stripe
		// (StripeWebhookController / StripeFulfillmentService) - elle ne marque plus jamais
		// le paiement comme paye, ne decremente plus le stock, n'effectue plus de
		// fulfillment, et ne fait plus confiance a un quelconque resultat de paiement lu
		// depuis le navigateur ou en interrogeant Stripe. Le nettoyage du panier est
		// desormais effectue par le fulfillment, pas ici (section 25).
		public IActionResult OrderConfirmation(int id)

		{

			OrderHeader orderHeader = _context.OrderHeaders
				.Include(o => o.OrderDetails)
				.Include(o => o.ShippingMethod)
				.Where(u => u.Id == id).FirstOrDefault();

			if (orderHeader == null)
			{
				return NotFound();
			}

			// Controle d'ownership obligatoire : un utilisateur authentifie ne doit jamais
			// pouvoir consulter la commande d'un autre utilisateur simplement en connaissant
			// son id (IDOR, SEC-004).
			var currentUserId = GetCurrentUserId();
			var isOwner = currentUserId != null && currentUserId == orderHeader.ApplicationUserId;
			if (!isOwner && !User.IsInRole("Admin"))
			{
				return Forbid();
			}

			// Invalidation du compteur de panier mis en cache en session (affichage
			// uniquement) : ne mute aucune donnee financiere ni le panier lui-meme.
			HttpContext.Session.Remove(SD.SessionCart);

			return View(orderHeader);

		}



		// Plus/Minus/Remove mutaient l'état via GET (aucune protection CSRF possible) et ne
		// vérifiaient jamais que le panier ciblé appartenait à l'appelant. Les deux défauts
		// sont corrigés ensemble : passage en POST + antiforgery, et la requête encode
		// directement le périmètre autorisé (Id == cartId AND ApplicationUserId == currentUserId)
		// au lieu d'un FirstOrDefault(Id == cartId) suivi d'un contrôle a posteriori.
		[HttpPost]
		[ValidateAntiForgeryToken]
		public IActionResult Plus(int cartId)

		{

			var currentUserId = GetCurrentUserId();

			var cartFromDb = _context.ShoppingCarts
				.FirstOrDefault(u => u.Id == cartId && u.ApplicationUserId == currentUserId);

			if (cartFromDb == null)
			{
				return NotFound();
			}

			cartFromDb.Count += 1;

			_context.ShoppingCarts.Update(cartFromDb);

			_context.SaveChanges();

			return RedirectToAction(nameof(Index));

		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public IActionResult Minus(int cartId)

		{

			var currentUserId = GetCurrentUserId();

			var cartFromDb = _context.ShoppingCarts
				.FirstOrDefault(u => u.Id == cartId && u.ApplicationUserId == currentUserId);

			if (cartFromDb == null)
			{
				return NotFound();
			}

			if (cartFromDb.Count <= 1)

			{

				_context.ShoppingCarts.Remove(cartFromDb);

				HttpContext.Session.SetInt32(SD.SessionCart, _context.ShoppingCarts

					.Where(u => u.ApplicationUserId == cartFromDb.ApplicationUserId).Count() - 1);

			}

			else

			{

				cartFromDb.Count -= 1;

				_context.ShoppingCarts.Update(cartFromDb);

			}

			_context.SaveChanges();

			return RedirectToAction(nameof(Index));

		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public IActionResult Remove(int cartId)

		{

			var currentUserId = GetCurrentUserId();

			var cartFromDb = _context.ShoppingCarts
				.FirstOrDefault(u => u.Id == cartId && u.ApplicationUserId == currentUserId);

			if (cartFromDb == null)
			{
				return NotFound();
			}

			_context.ShoppingCarts.Remove(cartFromDb);

			HttpContext.Session.SetInt32(SD.SessionCart, _context.ShoppingCarts

			  .Where(u => u.ApplicationUserId == cartFromDb.ApplicationUserId).Count() - 1);

			_context.SaveChanges();

			return RedirectToAction(nameof(Index));

		}

		private double GetPriceBasedOnQuantity(ShoppingCart shoppingCart)

		{

			return Convert.ToInt64(shoppingCart.Produit.Prix);

		}

		private string? GetCurrentUserId()
		{
			return (User.Identity as ClaimsIdentity)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		}

	}

}
