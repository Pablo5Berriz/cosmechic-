using System.Net;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // Matrice de tests COSMECHIC-SECURITY-001, section 16 - Cart (Plus/Minus/Remove).
    public class CartOwnershipTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public CartOwnershipTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        [Fact]
        public async Task Plus_OnOwnCart_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync($"/Cart/Plus?cartId={TestDataSeeder.ShoppingCartAId}", content: null);

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            var count = _factory.Query(ctx => ctx.ShoppingCarts.First(c => c.Id == TestDataSeeder.ShoppingCartAId).Count);
            Assert.Equal(3, count); // seedé à 2, +1
        }

        [Fact]
        public async Task Plus_OnAnotherCustomersCart_IsDeniedAndDoesNotMutate()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            // ShoppingCartBId appartient à Customer B.
            var response = await client.PostAsync($"/Cart/Plus?cartId={TestDataSeeder.ShoppingCartBId}", content: null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var count = _factory.Query(ctx => ctx.ShoppingCarts.First(c => c.Id == TestDataSeeder.ShoppingCartBId).Count);
            Assert.Equal(1, count); // inchangé
        }

        [Fact]
        public async Task Minus_OnAnotherCustomersCart_IsDeniedAndDoesNotMutate()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync($"/Cart/Minus?cartId={TestDataSeeder.ShoppingCartBId}", content: null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var stillExists = _factory.Query(ctx => ctx.ShoppingCarts.Any(c => c.Id == TestDataSeeder.ShoppingCartBId));
            Assert.True(stillExists);
        }

        [Fact]
        public async Task Remove_OnAnotherCustomersCart_IsDeniedAndDoesNotMutate()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync($"/Cart/Remove?cartId={TestDataSeeder.ShoppingCartBId}", content: null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var stillExists = _factory.Query(ctx => ctx.ShoppingCarts.Any(c => c.Id == TestDataSeeder.ShoppingCartBId));
            Assert.True(stillExists);
        }

        [Fact]
        public async Task Remove_OnOwnCart_IsAllowed()
        {
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.PostAsync($"/Cart/Remove?cartId={TestDataSeeder.ShoppingCartAId}", content: null);

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            var stillExists = _factory.Query(ctx => ctx.ShoppingCarts.Any(c => c.Id == TestDataSeeder.ShoppingCartAId));
            Assert.False(stillExists);
        }

        [Fact]
        public async Task Plus_Get_IsNoLongerAllowed()
        {
            // Anomalie corrigée (section 13) : Plus/Minus/Remove ne doivent plus muter
            // l'état via GET (impossible à protéger par CSRF).
            var client = _factory.CreateTestClient().AsCustomerA();

            var response = await client.GetAsync($"/Cart/Plus?cartId={TestDataSeeder.ShoppingCartAId}");

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        public void Dispose() => _factory.Dispose();
    }
}
