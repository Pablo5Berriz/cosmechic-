using System.IO;
using System.Text;
using System.Threading.Tasks;
using Cosmechic.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-SECURITY-002 (section 48/65) : preuve automatisée que ProductImageUploadService
    // applique bien l'allowlist extension+MIME+signature, la taille maximale, et ne laisse
    // jamais un nom de fichier client-contrôlé atteindre le système de fichiers.
    public class ImageUploadServiceTests
    {
        private static readonly byte[] JpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
        private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };

        private sealed class FakeWebHostEnvironment : IWebHostEnvironment
        {
            public string WebRootPath { get; set; } = "";
            public IFileProvider WebRootFileProvider { get; set; } = null!;
            public string ApplicationName { get; set; } = "Cosmechic.Tests";
            public IFileProvider ContentRootFileProvider { get; set; } = null!;
            public string ContentRootPath { get; set; } = "";
            public string EnvironmentName { get; set; } = "Development";
        }

        private static ProductImageUploadService CreateService(string webRoot, long maxBytes = 5 * 1024 * 1024)
        {
            var env = new FakeWebHostEnvironment { WebRootPath = webRoot };
            var options = Options.Create(new ImageUploadSettings { MaxFileSizeBytes = maxBytes });
            return new ProductImageUploadService(env, options);
        }

        private static IFormFile BuildFile(byte[] content, string fileName, string contentType)
        {
            var stream = new MemoryStream(content);
            return new FormFile(stream, 0, content.Length, "Image", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType,
            };
        }

        [Fact]
        public async Task SaveAsync_RejectsEmptyFile()
        {
            var dir = Directory.CreateTempSubdirectory().FullName;
            var service = CreateService(dir);
            var file = BuildFile(Array.Empty<byte>(), "empty.jpg", "image/jpeg");

            var result = await service.SaveAsync(file, "Images_Produits");

            Assert.Equal(ImageUploadOutcome.EmptyFile, result.Outcome);
        }

        [Fact]
        public async Task SaveAsync_RejectsFileAboveConfiguredLimit()
        {
            var dir = Directory.CreateTempSubdirectory().FullName;
            var service = CreateService(dir, maxBytes: 10);
            var content = new byte[] { 0xFF, 0xD8, 0xFF, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var file = BuildFile(content, "big.jpg", "image/jpeg");

            var result = await service.SaveAsync(file, "Images_Produits");

            Assert.Equal(ImageUploadOutcome.TooLarge, result.Outcome);
        }

        [Fact]
        public async Task SaveAsync_AcceptsFileExactlyAtLimit()
        {
            var dir = Directory.CreateTempSubdirectory().FullName;
            var content = new byte[20];
            JpegBytes.CopyTo(content, 0);
            var service = CreateService(dir, maxBytes: content.Length);
            var file = BuildFile(content, "exact.jpg", "image/jpeg");

            var result = await service.SaveAsync(file, "Images_Produits");

            Assert.Equal(ImageUploadOutcome.Success, result.Outcome);
        }

        [Theory]
        [InlineData(".exe")]
        [InlineData(".svg")]
        [InlineData(".php")]
        [InlineData(".cshtml")]
        [InlineData("")]
        public async Task SaveAsync_RejectsDisallowedExtensions(string extension)
        {
            var dir = Directory.CreateTempSubdirectory().FullName;
            var service = CreateService(dir);
            var file = BuildFile(JpegBytes, $"payload{extension}", "image/jpeg");

            var result = await service.SaveAsync(file, "Images_Produits");

            Assert.Equal(ImageUploadOutcome.InvalidExtension, result.Outcome);
        }

        [Fact]
        public async Task SaveAsync_RejectsContentTypeMismatchedWithExtension()
        {
            var dir = Directory.CreateTempSubdirectory().FullName;
            var service = CreateService(dir);
            var file = BuildFile(JpegBytes, "image.jpg", "image/png");

            var result = await service.SaveAsync(file, "Images_Produits");

            Assert.Equal(ImageUploadOutcome.InvalidContentType, result.Outcome);
        }

        [Fact]
        public async Task SaveAsync_RejectsRenamedMalwareMasqueradingAsImage()
        {
            // Fichier .cshtml renommé en .jpg avec un Content-Type falsifié : la vérification
            // par signature binaire doit rejeter ce qui n'a pas les octets magiques JPEG.
            var dir = Directory.CreateTempSubdirectory().FullName;
            var service = CreateService(dir);
            var malwareContent = Encoding.UTF8.GetBytes("@{ System.Diagnostics.Process.Start(\"cmd\"); }");
            var file = BuildFile(malwareContent, "malware.jpg", "image/jpeg");

            var result = await service.SaveAsync(file, "Images_Produits");

            Assert.Equal(ImageUploadOutcome.InvalidSignature, result.Outcome);
        }

        [Theory]
        [InlineData("../../../etc/passwd.jpg")]
        [InlineData("..\\..\\evil.jpg")]
        [InlineData("/etc/passwd.jpg")]
        public async Task SaveAsync_NeverUsesClientSuppliedFileNameForPathTraversalAttempts(string maliciousName)
        {
            var dir = Directory.CreateTempSubdirectory().FullName;
            var service = CreateService(dir);
            var file = BuildFile(JpegBytes, maliciousName, "image/jpeg");

            var result = await service.SaveAsync(file, "Images_Produits");

            Assert.Equal(ImageUploadOutcome.Success, result.Outcome);
            Assert.NotNull(result.StoredFileName);
            // Le nom stocké est un GUID généré côté serveur : aucune trace du nom d'origine,
            // aucun séparateur de chemin.
            Assert.DoesNotContain("..", result.StoredFileName);
            Assert.DoesNotContain("/", result.StoredFileName);
            Assert.DoesNotContain("\\", result.StoredFileName);
            Assert.DoesNotContain("etc", result.StoredFileName);
            Assert.DoesNotContain("evil", result.StoredFileName);
            Assert.True(Guid.TryParse(Path.GetFileNameWithoutExtension(result.StoredFileName), out _));

            var savedPath = Path.Combine(dir, "Images_Produits", result.StoredFileName!);
            Assert.True(File.Exists(savedPath));
            var resolvedDir = Path.GetFullPath(Path.Combine(dir, "Images_Produits"));
            Assert.StartsWith(resolvedDir, Path.GetFullPath(savedPath));
        }

        [Fact]
        public async Task SaveAsync_AcceptsValidJpegPngAndProducesUniqueNamesWithoutOverwrite()
        {
            var dir = Directory.CreateTempSubdirectory().FullName;
            var service = CreateService(dir);

            var jpeg = await service.SaveAsync(BuildFile(JpegBytes, "a.jpg", "image/jpeg"), "Images_Produits");
            var png = await service.SaveAsync(BuildFile(PngBytes, "b.png", "image/png"), "Images_Produits");
            var jpeg2 = await service.SaveAsync(BuildFile(JpegBytes, "a.jpg", "image/jpeg"), "Images_Produits");

            Assert.True(jpeg.Succeeded);
            Assert.True(png.Succeeded);
            Assert.True(jpeg2.Succeeded);
            Assert.NotEqual(jpeg.StoredFileName, jpeg2.StoredFileName);
        }

        [Fact]
        public async Task SaveAsync_RejectsPngExtensionWithJpegBytes()
        {
            var dir = Directory.CreateTempSubdirectory().FullName;
            var service = CreateService(dir);
            var file = BuildFile(JpegBytes, "fake.png", "image/png");

            var result = await service.SaveAsync(file, "Images_Produits");

            Assert.Equal(ImageUploadOutcome.InvalidSignature, result.Outcome);
        }
    }
}
