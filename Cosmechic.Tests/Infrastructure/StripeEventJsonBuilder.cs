namespace Cosmechic.Tests.Infrastructure
{
    // Construit localement un payload JSON d'événement Stripe checkout.session.* pour les
    // tests du webhook — jamais un appel réseau réel à Stripe (COSMECHIC-ECOM-CORE-001,
    // section 33/37). La forme suit exactement celle des payloads Stripe réels
    // (nécessaire : Stripe.net échoue si des champs comme api_version sont absents).
    public static class StripeEventJsonBuilder
    {
        public static string CheckoutSessionEvent(
            string eventId,
            string eventType,
            string sessionId,
            int? orderId,
            long amountTotal,
            string currency = "cad",
            string paymentStatus = "paid",
            string paymentIntentId = "pi_test")
        {
            var metadata = orderId.HasValue
                ? $$"""{ "OrderId": "{{orderId}}" }"""
                : "{}";

            return $$"""
            {
              "id": "{{eventId}}",
              "object": "event",
              "api_version": "2020-08-27",
              "created": 1700000000,
              "livemode": false,
              "pending_webhooks": 0,
              "request": { "id": null, "idempotency_key": null },
              "type": "{{eventType}}",
              "data": {
                "object": {
                  "id": "{{sessionId}}",
                  "object": "checkout.session",
                  "payment_status": "{{paymentStatus}}",
                  "amount_total": {{amountTotal}},
                  "currency": "{{currency}}",
                  "payment_intent": "{{paymentIntentId}}",
                  "metadata": {{metadata}}
                }
              }
            }
            """;
        }

        public static string UnsupportedEvent(string eventId, string eventType = "customer.created")
            => $$"""
            {
              "id": "{{eventId}}",
              "object": "event",
              "api_version": "2020-08-27",
              "created": 1700000000,
              "livemode": false,
              "pending_webhooks": 0,
              "request": { "id": null, "idempotency_key": null },
              "type": "{{eventType}}",
              "data": { "object": { "id": "cus_test", "object": "customer" } }
            }
            """;
    }
}
