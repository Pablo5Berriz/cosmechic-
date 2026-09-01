using Microsoft.AspNetCore.Http;

namespace Cosmechic.Services
{
    public class CspNonceAccessor(IHttpContextAccessor httpContextAccessor) : ICspNonceAccessor
    {
        public string Nonce => httpContextAccessor.HttpContext?.Items[CspNonceItemsKey] as string ?? string.Empty;

        public const string CspNonceItemsKey = "CspNonce";
    }
}
