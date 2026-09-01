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

	public class CartController(CosmechicsContext context, ICheckoutService checkoutService, ICancellationService cancellationService, IAddressService addressService) : Controller
	{

		private CosmechicsContext _context = context;
		private readonly ICheckoutService _checkoutService = checkoutService;
		private readonly ICancellationService _cancellationService = cancellationService;
		private readonly IAddressService _addressService = addressService;

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
        public async Task<IActionResult> Summary()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var applicationUser = _context.AspNetUsers.FirstOrDefault(u => u.Id == userId);
            var savedAddresses = await _addressService.ListForUserAsync(userId);
            var defaultAddress = savedAddresses.FirstOrDefault(a => a.IsDefaultShipping);

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
                SavedAddresses = savedAddresses,
                Input = new CheckoutFormInput
                {
                    // COSMECHIC-ACCOUNT-001 (section 15) : préremplissage par l'adresse de
                    // livraison par défaut si elle existe, plutôt que Name/PhoneNumber
                    // seuls comme avant ce lot.
                    SelectedAddressId = defaultAddress?.Id,
                    Name = defaultAddress?.RecipientName ?? applicationUser?.UserName,
                    PhoneNumber = defaultAddress?.PhoneNumber ?? applicationUser?.PhoneNumber,
                    StreetAddress = defaultAddress?.StreetAddress,
                    City = defaultAddress?.City,
                    State = defaultAddress?.State,
                    PostalCode = defaultAddress?.PostalCode,
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

			ShippingAddress shipping;
			if (input?.SelectedAddressId is int selectedAddressId)
			{
				// COSMECHIC-ACCOUNT-001 (section 15/28) : ownership vérifié ici — un
				// SelectedAddressId appartenant à un autre client échoue silencieusement
				// vers "aucune correspondance" plutôt que de divulguer/utiliser l'adresse
				// d'autrui (IDOR).
				var savedAddress = await _addressService.GetOwnedAsync(selectedAddressId, userId);
				if (savedAddress == null)
				{
					TempData["error"] = "Adresse sélectionnée introuvable.";
					return RedirectToAction(nameof(Summary));
				}

				shipping = new ShippingAddress(
					savedAddress.RecipientName,
					savedAddress.PhoneNumber,
					savedAddress.StreetAddress,
					savedAddress.City,
					savedAddress.State,
					savedAddress.PostalCode,
					input?.ShippingMethodId ?? 0);
			}
			else
			{
				shipping = new ShippingAddress(
					input?.Name ?? string.Empty,
					input?.PhoneNumber ?? string.Empty,
					input?.StreetAddress ?? string.Empty,
					input?.City ?? string.Empty,
					input?.State ?? string.Empty,
					input?.PostalCode ?? string.Empty,
					input?.ShippingMethodId ?? 0);
			}

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

		// COSMECHIC-COMMERCE-OPERATIONS-001B (section 46/76) : reçu durable/imprimable,
		// distinct de la confirmation immédiate — même contrôle d'ownership (owner ou
		// Admin), snapshot financier persisté uniquement (jamais recalculé).
		public IActionResult Receipt(int id)
		{
			OrderHeader orderHeader = _context.OrderHeaders
				.Include(o => o.OrderDetails).ThenInclude(d => d.Produit)
				.Include(o => o.ShippingMethod)
				.FirstOrDefault(o => o.Id == id);

			if (orderHeader == null)
			{
				return NotFound();
			}

			var currentUserId = GetCurrentUserId();
			var isOwner = currentUserId != null && currentUserId == orderHeader.ApplicationUserId;
			if (!isOwner && !User.IsInRole("Admin"))
			{
				return Forbid();
			}

			return View(orderHeader);
		}

		// COSMECHIC-COMMERCE-OPERATIONS-001B (section 13/14/51) : annulation client de sa
		// propre commande — délègue entièrement à ICancellationService (ownership,
		// politique de blocage, workflow de remboursement le cas échéant) ; ne touche
		// jamais directement OrderStatus/PaymentStatus ici.
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CancelOrder(int orderId)
		{
			var userId = GetCurrentUserId();
			if (userId == null)
			{
				return Unauthorized();
			}

			var result = await _cancellationService.CancelOrderAsync(orderId, userId, isAdmin: false, reason: "Annulation demandée par le client.");
			if (result is CancellationRejected rejected)
			{
				TempData["error"] = rejected.Reason;
			}

			return RedirectToAction("Details", "OrderHeaders", new { id = orderId });
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
