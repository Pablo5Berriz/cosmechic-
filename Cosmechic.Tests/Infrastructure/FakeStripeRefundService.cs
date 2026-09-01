using Cosmechic.Services;
using Stripe;

namespace Cosmechic.Tests.Infrastructure
{
    // Double de test pour IStripeRefundService : aucun appel réseau réel à Stripe
    // (COSMECHIC-COMMERCE-OPERATIONS-001B, section 22 — REAL_STRIPE_USED=NO). Enregistre la
    // dernière requête pour permettre aux tests de vérifier IdempotencyKey/Amount/PaymentIntent
    // sans jamais y toucher réellement, et simule un statut Stripe configurable
    // (succeeded/failed) pour les scénarios d'échec/retry.
    public class FakeStripeRefundService : IStripeRefundService
    {
        public int CallCount { get; private set; }
        public RefundCreateOptions? LastOptions { get; private set; }
        public RequestOptions? LastRequestOptions { get; private set; }
        public readonly List<string> IdempotencyKeysSeen = new();

        // Unique par instance (jamais une constante partagée) : plusieurs tests dans la
        // même collection SqlServerFixture partagent une base physique réelle où
        // IX_Refunds_StripeRefundId est une vraie contrainte UNIQUE — une valeur fixe
        // provoquerait une collision inter-tests non liée au comportement testé.
        public string StripeRefundIdToReturn { get; set; } = $"re_test_{Guid.NewGuid():N}";
        public string StatusToReturn { get; set; } = "succeeded";
        public Exception? ExceptionToThrow { get; set; }

        public Refund CreateRefund(RefundCreateOptions options, RequestOptions requestOptions)
        {
            CallCount++;
            LastOptions = options;
            LastRequestOptions = requestOptions;
            if (requestOptions.IdempotencyKey != null)
            {
                IdempotencyKeysSeen.Add(requestOptions.IdempotencyKey);
            }

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            return new Refund
            {
                Id = StripeRefundIdToReturn,
                Status = StatusToReturn,
                Amount = options.Amount ?? 0,
                PaymentIntentId = options.PaymentIntent,
            };
        }
    }
}
