using Cosmechic.Models;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe.Checkout;

namespace Cosmechic.Services
{
    // Coordonnées de livraison explicitement autorisées à venir du client. Volontairement
    // distinct de OrderHeader : aucune valeur financière ou d'état (OrderTotal, Price,
    // PaymentStatus, OrderStatus, SessionId, PaymentIntentId, ApplicationUserId) ne
    // transite par ce DTO — COSMECHIC-ECOM-CORE-001 section 8. L'ancien
    // CartController.SummaryPOST liait ces champs via [BindProperty] sur ShoppingCartVM
    // en entier (sur-liaison / mass-assignment), neutralisée jusqu'ici seulement par des
    // écrasements a posteriori fragiles (sauf OrderTotal, qui utilisait `+=` sur une valeur
    // potentiellement fournie par le client au lieu de `=`).
    public record ShippingAddress(
        string Name,
        string PhoneNumber,
        string StreetAddress,
        string City,
        string State,
        string PostalCode);

    public abstract record CheckoutResult;
    public sealed record CheckoutSessionCreated(int OrderHeaderId, string RedirectUrl) : CheckoutResult;
    public sealed record CheckoutFailed(string Reason) : CheckoutResult;

    public interface ICheckoutService
    {
        Task<CheckoutResult> CreateCheckoutSessionAsync(string userId, ShippingAddress shipping, string domain);
    }

    // COSMECHIC-ECOM-CORE-001 (sections 8, 9, 10, 28) : seule source de vérité pour la
    // création d'une commande et de sa session Stripe. Recalcule tout ce qui constitue une
    // valeur financière depuis la base (jamais depuis le client), snapshot le nom/prix du
    // produit sur OrderDetail pour l'intégrité historique (préparé par COSMECHIC-DATA-001),
    // et associe la commande à Stripe via un identifiant de commande en metadata plutôt
    // que via une valeur devinable/manipulable côté client.
    public class OrderCheckoutService(
        CosmechicsContext context,
        IStripeCheckoutService stripeCheckoutService,
        ILogger<OrderCheckoutService> logger) : ICheckoutService
    {
        public async Task<CheckoutResult> CreateCheckoutSessionAsync(string userId, ShippingAddress shipping, string domain)
        {
            var cartItems = await context.ShoppingCarts
                .Where(c => c.ApplicationUserId == userId)
                .Include(c => c.Produit)
                .ToListAsync();

            if (cartItems.Count == 0)
            {
                return new CheckoutFailed("Le panier est vide.");
            }

            foreach (var item in cartItems)
            {
                if (!CartQuantityPolicy.IsValidRequestedQuantity(item.Count))
                {
                    return new CheckoutFailed($"Quantité invalide pour {item.Produit.Nom}.");
                }

                if (!item.Produit.Disponible)
                {
                    return new CheckoutFailed($"{item.Produit.Nom} n'est plus disponible.");
                }

                // Vérification informative : évite de créer une session Stripe pour une
                // commande manifestement irréalisable. Le contrôle déterminant contre la
                // survente reste celui, transactionnel et concurrentiel-sûr, du fulfillment
                // (StripeFulfillmentService) — jamais celui-ci.
                if (item.Produit.Stock < item.Count)
                {
                    return new CheckoutFailed($"Stock insuffisant pour {item.Produit.Nom}.");
                }
            }

            var orderTotal = cartItems.Sum(item => item.Produit.Prix * item.Count);

            var orderHeader = new OrderHeader
            {
                ApplicationUserId = userId,
                OrderDate = DateTime.UtcNow,
                OrderTotal = orderTotal,
                OrderStatus = SD.StatusPending,
                PaymentStatus = SD.PaymentStatusPending,
                Name = shipping.Name,
                PhoneNumber = shipping.PhoneNumber,
                StreetAddress = shipping.StreetAddress,
                City = shipping.City,
                State = shipping.State,
                PostalCode = shipping.PostalCode,
            };

            foreach (var item in cartItems)
            {
                orderHeader.OrderDetails.Add(new OrderDetail
                {
                    ProduitId = item.ProduitId,
                    Count = item.Count,
                    Price = item.Produit.Prix,
                    ProduitNom = item.Produit.Nom,
                });
            }

            await using var transaction = await context.Database.BeginTransactionAsync();
            context.OrderHeaders.Add(orderHeader);
            await context.SaveChangesAsync();

            var options = new SessionCreateOptions
            {
                SuccessUrl = domain + $"cart/OrderConfirmation?id={orderHeader.Id}",
                CancelUrl = domain + "cart/index",
                Mode = "payment",
                LineItems = cartItems.Select(item => new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)Math.Round(item.Produit.Prix * 100, MidpointRounding.AwayFromZero),
                        Currency = CheckoutConstants.Currency,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = item.Produit.Nom,
                        },
                    },
                    Quantity = item.Count,
                }).ToList(),
                Metadata = new Dictionary<string, string>
                {
                    ["OrderId"] = orderHeader.Id.ToString(),
                },
            };

            Session session;
            try
            {
                session = stripeCheckoutService.CreateSession(options);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Échec de création de session Stripe pour la commande {OrderId}", orderHeader.Id);
                await transaction.RollbackAsync();
                return new CheckoutFailed("Impossible de démarrer le paiement pour le moment.");
            }

            orderHeader.SessionId = session.Id;
            if (!string.IsNullOrEmpty(session.PaymentIntentId))
            {
                orderHeader.PaymentIntentId = session.PaymentIntentId;
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            logger.LogInformation("Session Stripe {SessionId} créée pour la commande {OrderId}", session.Id, orderHeader.Id);

            return new CheckoutSessionCreated(orderHeader.Id, session.Url);
        }
    }
}
