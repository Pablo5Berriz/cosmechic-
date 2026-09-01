using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Cosmechic.Tests.Infrastructure
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B-CLOSURE-1 : les tests HTTP de bout en bout qui
    // téléversent une vraie image (CatalogAdminTests, via le pipeline ASP.NET Core réel)
    // résolvent IWebHostEnvironment.WebRootPath vers le VRAI wwwroot du projet, laissant
    // des fichiers réels sous Cosmechic/wwwroot/Images_Produits après chaque exécution —
    // artefact reproductible, jusqu'ici nettoyé manuellement. Ce décorateur redirige
    // uniquement WebRootPath vers un répertoire temporaire jetable, propre à chaque
    // instance de CustomWebApplicationFactory : ProductImageUploadService (seul
    // consommateur de WebRootPath dans le code produit) écrit désormais exclusivement dans
    // cet espace isolé. WebRootFileProvider reste délégué à l'environnement réel, donc le
    // service de fichiers statiques (assets existants du projet) n'est pas affecté — aucun
    // comportement de production n'est modifié, seule la destination d'écriture change en
    // test.
    internal sealed class IsolatedWebRootEnvironment(IWebHostEnvironment inner, string isolatedWebRootPath) : IWebHostEnvironment
    {
        public string WebRootPath
        {
            get => isolatedWebRootPath;
            set => inner.WebRootPath = value;
        }

        public IFileProvider WebRootFileProvider
        {
            get => inner.WebRootFileProvider;
            set => inner.WebRootFileProvider = value;
        }

        public string ApplicationName
        {
            get => inner.ApplicationName;
            set => inner.ApplicationName = value;
        }

        public IFileProvider ContentRootFileProvider
        {
            get => inner.ContentRootFileProvider;
            set => inner.ContentRootFileProvider = value;
        }

        public string ContentRootPath
        {
            get => inner.ContentRootPath;
            set => inner.ContentRootPath = value;
        }

        public string EnvironmentName
        {
            get => inner.EnvironmentName;
            set => inner.EnvironmentName = value;
        }
    }
}
