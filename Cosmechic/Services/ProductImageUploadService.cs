using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Cosmechic.Services
{
    public class ProductImageUploadService(
        IWebHostEnvironment hostingEnvironment,
        IOptions<ImageUploadSettings> options) : IProductImageUploadService
    {
        // Allowlist stricte (section 7) : uniquement les formats réellement utilisés
        // pour des images produit/catégorie. SVG explicitement exclu (peut contenir du
        // script), tout comme tout format exécutable/interprétable côté serveur.
        private static readonly IReadOnlyDictionary<string, string> AllowedExtensionToMimeType =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".jpg"] = "image/jpeg",
                [".jpeg"] = "image/jpeg",
                [".png"] = "image/png",
                [".webp"] = "image/webp",
            };

        public async Task<ImageUploadResult> SaveAsync(IFormFile file, string subfolder)
        {
            if (file.Length == 0)
            {
                return new ImageUploadResult(ImageUploadOutcome.EmptyFile, null);
            }

            var settings = options.Value;
            if (file.Length > settings.MaxFileSizeBytes)
            {
                return new ImageUploadResult(ImageUploadOutcome.TooLarge, null);
            }

            // L'extension du nom soumis par le client n'est utilisée QUE pour choisir
            // parmi l'allowlist ci-dessus — jamais recopiée telle quelle dans le nom de
            // fichier final ni dans le chemin (section 6/11 : aucun path traversal
            // possible, le nom stocké est intégralement généré côté serveur).
            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(extension) || !AllowedExtensionToMimeType.TryGetValue(extension, out var expectedMimeType))
            {
                return new ImageUploadResult(ImageUploadOutcome.InvalidExtension, null);
            }

            // Le Content-Type est fourni par le client et donc falsifiable — vérifié en
            // plus de la signature binaire, jamais à sa place seule (section 8).
            if (!string.Equals(file.ContentType, expectedMimeType, StringComparison.OrdinalIgnoreCase))
            {
                return new ImageUploadResult(ImageUploadOutcome.InvalidContentType, null);
            }

            if (!await HasValidSignatureAsync(file, extension))
            {
                return new ImageUploadResult(ImageUploadOutcome.InvalidSignature, null);
            }

            var uploadsFolder = Path.Combine(hostingEnvironment.WebRootPath, subfolder);
            Directory.CreateDirectory(uploadsFolder);

            var normalizedExtension = extension.ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}{normalizedExtension}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            // FileMode.CreateNew : jamais d'écrasement silencieux d'un fichier existant
            // (section 12/14) — une collision de GUID est virtuellement impossible, donc
            // un conflit ici indiquerait une anomalie plutôt qu'un remplacement légitime.
            await using var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write);
            await using (var sourceStream = file.OpenReadStream())
            {
                await sourceStream.CopyToAsync(fileStream);
            }

            return new ImageUploadResult(ImageUploadOutcome.Success, fileName);
        }

        private static async Task<bool> HasValidSignatureAsync(IFormFile file, string extension)
        {
            await using var stream = file.OpenReadStream();
            var header = new byte[12];
            var read = await stream.ReadAsync(header.AsMemory(0, header.Length));

            return extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
                ".png" => read >= 8
                    && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                    && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
                // WEBP : "RIFF" (0-3), taille sur 4 octets (4-7), "WEBP" (8-11).
                ".webp" => read >= 12
                    && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
                    && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P',
                _ => false,
            };
        }
    }
}
