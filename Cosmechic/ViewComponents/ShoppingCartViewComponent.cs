using Cosmechic.Models;
using Cosmechic.Utility;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace BulkyBookWeb.ViewComponents
{
    public class ShoppingCartViewComponent : ViewComponent
    {

        private readonly CosmechicsContext _context;
        private readonly ILogger<ShoppingCartViewComponent> _logger;

        public ShoppingCartViewComponent(CosmechicsContext context, ILogger<ShoppingCartViewComponent> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);

            if (claim != null)
            {
                if (HttpContext.Session.GetInt32(SD.SessionCart) == null)
                {
                    // COSMECHIC-SECURITY-002 (section 13) : ce composant est rendu sur
                    // TOUTES les pages via _Layout.cshtml, y compris /Home/Error. Si la
                    // base de données est injoignable, une exception non interceptée ici
                    // ferait s'effondrer la page d'erreur elle-même (deuxième exception à
                    // l'intérieur du pipeline de gestion d'erreur). On dégrade donc vers un
                    // compteur à 0 plutôt que de laisser l'exception se propager.
                    try
                    {
                        HttpContext.Session.SetInt32(SD.SessionCart,
                            _context.ShoppingCarts.Where(u => u.ApplicationUserId == claim.Value).Count());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Impossible de calculer le compteur du panier (base de données injoignable).");
                        return View(0);
                    }
                }

                return View(HttpContext.Session.GetInt32(SD.SessionCart));
            }
            else
            {
                HttpContext.Session.Clear();
                return View(0);
            }
        }

    }
}
