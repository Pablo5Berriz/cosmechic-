namespace Cosmechic.Services
{
    // COSMECHIC-BUSINESS-POLICY-001 (section 9B) : PRODUCTION_DOMAIN approuvé par le PM
    // (https://cosmechic.ca) — seule source de vérité pour construire des URLs absolues
    // (sitemap.xml, balise canonical) qui ne peuvent pas être dérivées dynamiquement de la
    // requête courante (contrairement aux callbacks Identity, voir COSMECHIC-RELEASE-
    // CONFIG-001 section 8, qui restent dérivés de Request.Scheme/Host). Vide par défaut :
    // un environnement où elle n'est pas configurée (développement, tests) ne génère jamais
    // une URL absolue fabriquée — voir SitemapController/canonical partial pour le
    // comportement explicite dans ce cas.
    public class ApplicationOptions
    {
        public string PublicBaseUrl { get; set; } = string.Empty;
    }
}
