using Cosmechic.Models;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cosmechic.Services
{
    public class RestockService(CosmechicsContext context, ILogger<RestockService> logger) : IRestockService
    {
        private const int MaxConcurrencyAttempts = 3;

        public async Task<RestockResult> CompleteRestockAsync(int returnItemId, string actorUserId)
        {
            for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
            {
                var returnItem = await context.ReturnItems
                    .Include(ri => ri.ReturnRequest)
                    .Include(ri => ri.OrderDetail).ThenInclude(od => od.Produit)
                    .FirstOrDefaultAsync(ri => ri.Id == returnItemId);

                if (returnItem == null)
                {
                    return new RestockRejected("Ligne de retour introuvable.");
                }

                // Idempotence (section 39) : une même unité retournée ne peut être remise en
                // stock qu'une fois — no-op explicite, pas une erreur, si déjà fait.
                if (returnItem.Restocked)
                {
                    return new RestockAlreadyDone();
                }

                if (returnItem.ReturnRequest.Status is not (SD.ReturnStatusReceived or SD.ReturnStatusCompleted))
                {
                    return new RestockRejected("Le retour doit avoir été reçu avant toute remise en stock.");
                }

                var produit = returnItem.OrderDetail.Produit;
                produit.Stock += returnItem.Quantity;
                returnItem.Restocked = true;
                returnItem.RestockedAt = DateTime.UtcNow;

                context.StockMovements.Add(new StockMovement
                {
                    ProduitId = produit.ProduitId,
                    QuantityDelta = returnItem.Quantity,
                    Reason = SD.StockMovementReasonReturnRestock,
                    OrderId = returnItem.OrderDetail.OrderHeaderId,
                    ReturnItemId = returnItem.Id,
                    ActorUserId = actorUserId,
                    ActorType = SD.ActorTypeAdmin,
                    CreatedAt = DateTime.UtcNow,
                });

                try
                {
                    await context.SaveChangesAsync();
                    return new RestockCompleted();
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyAttempts)
                {
                    logger.LogWarning(
                        "Conflit de concurrence RowVersion à la remise en stock du ReturnItem {ReturnItemId}, nouvelle tentative ({Attempt}/{Max})",
                        returnItemId, attempt + 1, MaxConcurrencyAttempts);
                    context.ChangeTracker.Clear();
                }
            }

            logger.LogError("Conflits de concurrence répétés à la remise en stock du ReturnItem {ReturnItemId}", returnItemId);
            return new RestockRejected("Conflit de concurrence répété.");
        }
    }
}
