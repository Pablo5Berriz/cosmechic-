using Microsoft.AspNetCore.Http;

namespace Cosmechic.Services
{
    public enum ImageUploadOutcome
    {
        Success,
        EmptyFile,
        TooLarge,
        InvalidExtension,
        InvalidContentType,
        InvalidSignature,
    }

    public record ImageUploadResult(ImageUploadOutcome Outcome, string? StoredFileName)
    {
        public bool Succeeded => Outcome == ImageUploadOutcome.Success;
    }

    // COSMECHIC-SECURITY-002 (SEC-007, sections 5-14) : point unique de validation et
    // d'enregistrement des images produit/catégorie, partagé par ProduitsController et
    // CategoriesController (auparavant : quatre copies indépendantes du même code non
    // validé — nom de fichier client utilisé tel quel, aucune vérification d'extension/
    // MIME/signature/taille).
    public interface IProductImageUploadService
    {
        Task<ImageUploadResult> SaveAsync(IFormFile file, string subfolder);
    }
}
