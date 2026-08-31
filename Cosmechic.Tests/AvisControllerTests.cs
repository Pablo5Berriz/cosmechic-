using System.Net;
using System.Net.Http;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // Matrice de tests COSMECHIC-SECURITY-001, section 16 - Avis.
    public class AvisControllerTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public AvisControllerTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        [Fact]
        public async Task Index_Anonymous_IsAllowed()
        {
            // Lecture publique volontaire et justifiée (section 8) : contenu de découverte
            // produit, sans donnée personnelle sensible exposée (vues vérifiées).
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/Avis/Index");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Details_Anonymous_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync($"/Avis/Details/{TestDataSeeder.AviAId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Create_Anonymous_IsDenied()
        {
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.GetAsync("/Avis/Create");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Create_Post_Anonymous_IsDenied()
        {
            var client = _factory.CreateTestClient().AsAnonymous();
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ProduitId"] = TestDataSeeder.ProduitId.ToString(),
                ["Note"] = "5",
                ["DateReview"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            });

            var response = await client.PostAsync("/Avis/Create", form);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Edit_Owner_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/Avis/Edit/{TestDataSeeder.AviAId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Edit_ForeignUser_IsDenied()
        {
            // AviAId appartient à Customer A.
            var client = _factory.CreateTestClient().AsCustomerB();

            var response = await client.GetAsync($"/Avis/Edit/{TestDataSeeder.AviAId}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Delete_Owner_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var getResponse = await client.GetAsync($"/Avis/Delete/{TestDataSeeder.AviAId}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var postResponse = await client.PostAsync($"/Avis/Delete/{TestDataSeeder.AviAId}", content: null);
            // Autorisé à agir : ne doit jamais être bloqué par le contrôle d'ownership.
            Assert.NotEqual(HttpStatusCode.Forbidden, postResponse.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, postResponse.StatusCode);

            var stillExists = _factory.Query(ctx => ctx.Avis.Any(a => a.ReviewId == TestDataSeeder.AviAId));
            Assert.False(stillExists);
        }

        [Fact]
        public async Task Delete_ForeignUser_IsDenied()
        {
            var client = _factory.CreateTestClient().AsCustomerB();

            var getResponse = await client.GetAsync($"/Avis/Delete/{TestDataSeeder.AviAId}");
            Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

            var postResponse = await client.PostAsync($"/Avis/Delete/{TestDataSeeder.AviAId}", content: null);
            Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);

            var stillExists = _factory.Query(ctx => ctx.Avis.Any(a => a.ReviewId == TestDataSeeder.AviAId));
            Assert.True(stillExists);
        }

        public void Dispose() => _factory.Dispose();
    }
}
