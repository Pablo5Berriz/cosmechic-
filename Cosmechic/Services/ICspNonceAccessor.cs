namespace Cosmechic.Services
{
    // COSMECHIC-SECURITY-002 (section 12) : expose le nonce CSP généré par requête
    // (Program.cs) aux vues Razor, pour que les rares blocs <script> inline puissent
    // s'exécuter sous une CSP script-src sans 'unsafe-inline'.
    public interface ICspNonceAccessor
    {
        string Nonce { get; }
    }
}
