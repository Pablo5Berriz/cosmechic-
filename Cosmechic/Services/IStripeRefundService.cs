using Stripe;

namespace Cosmechic.Services
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 21/22) : le controller/orchestrateur ne
    // doit jamais appeler le SDK Stripe directement — même seam que IStripeCheckoutService
    // (ECOM-CORE-001), pour permettre un double de test sans aucun appel réseau réel.
    public interface IStripeRefundService
    {
        Refund CreateRefund(RefundCreateOptions options, RequestOptions requestOptions);
    }
}
