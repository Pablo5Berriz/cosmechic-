using System.Security.Cryptography;
using System.Text;

namespace Cosmechic.Tests.Infrastructure
{
    // Génère un en-tête Stripe-Signature localement, selon l'algorithme HMAC-SHA256
    // publiquement documenté par Stripe, pour tester la vérification de signature du
    // webhook sans jamais appeler Stripe (COSMECHIC-ECOM-CORE-001, section 33/37).
    public static class StripeSignatureTestHelper
    {
        public static string SignPayload(string payload, string secret, long? unixTimestamp = null)
        {
            var timestamp = unixTimestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signedPayload = $"{timestamp}.{payload}";

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
            var signatureHex = Convert.ToHexString(hash).ToLowerInvariant();

            return $"t={timestamp},v1={signatureHex}";
        }
    }
}
