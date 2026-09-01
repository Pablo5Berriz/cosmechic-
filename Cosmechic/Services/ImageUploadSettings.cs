namespace Cosmechic.Services
{
    // COSMECHIC-SECURITY-002 (section 9) : taille maximale configurable pour les images
    // produit/catégorie, jamais illimitée ni codée en dur dans le service.
    public class ImageUploadSettings
    {
        public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024; // 5 Mo par défaut
    }
}
