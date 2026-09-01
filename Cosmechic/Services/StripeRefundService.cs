using Stripe;

namespace Cosmechic.Services
{
    // Implémentation par défaut : délègue directement au SDK Stripe.net, comportement
    // de production seul appelant réel de Stripe pour les remboursements.
    public class StripeRefundService : IStripeRefundService
    {
        public Refund CreateRefund(RefundCreateOptions options, RequestOptions requestOptions)
        {
            var service = new RefundService();
            return service.Create(options, requestOptions);
        }
    }
}
