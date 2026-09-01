using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Services
{
    // Extrait de StripeFulfillmentService (ECOM-CORE-001) : partagé avec
    // RefundOrchestrationService, qui applique la même barrière d'idempotence
    // (ProcessedStripeEvent) pour les événements Stripe de remboursement.
    internal static class SqlServerErrors
    {
        public static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            // SQL Server : 2627 = violation de contrainte UNIQUE/PK, 2601 = index unique
            // dupliqué. InMemory ne reproduit pas cette erreur (voir tests SQL Server dédiés).
            return ex.InnerException is SqlException sqlEx && sqlEx.Number is 2627 or 2601;
        }
    }
}
