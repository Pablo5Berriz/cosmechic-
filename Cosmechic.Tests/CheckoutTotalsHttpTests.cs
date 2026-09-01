using System.Net;
using Cosmechic.Models;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 41/47) : bout en bout HTTP au travers du
    // vrai pipeline MVC (liaison de modèle incluse). Prouve que CheckoutFormInput ne peut
    // physiquement porter aucune valeur financière/état : poster des champs supplémentaires
    // (OrderTotal, TaxAmount, ShippingAmount, PaymentStatus, ApplicationUserId...) n'a
    // littéralement aucun effet, faute de propriété où ils pourraient se lier.
    public class CheckoutTotalsHttpTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        public CheckoutTotalsHttpTests()
        {
            TestDataSeeder.SeedStandardFixture(_factory);
        }

        private int SeedShippingMethod(decimal price = 15.00m, bool isActive = true)
        {
            var id = 0;
            _factory.Seed(context =>
            {
                var method = new ShippingMethod { Name = "Livraison standard", Price = price, IsActive = isActive, SortOrder = 1 };
                context.ShippingMethods.Add(method);
                context.SaveChanges();
                id = method.ShippingMethodId;
            });
            return id;
        }

        // COSMECHIC-COMMERCE-OPERATIONS-001A (section 5) : Program.cs amorce déjà TPS/TVQ
        // au démarrage de l'hôte de test (CommerceSeedService, comme en production) — ne
        // ré-ajoute rien s'il l'a déjà fait, pour ne jamais compter les mêmes taux deux fois.
        private void SeedTaxRates()
        {
            _factory.Seed(context =>
            {
                if (context.TaxRates.Any())
                {
                    return;
                }

                context.TaxRates.Add(new TaxRate
                {
                    Jurisdiction = "TPS (fédérale)",
                    CountryCode = "CA",
                    RegionCode = null,
                    Rate = 0.05m,
                    EffectiveFrom = DateTime.UtcNow.AddYears(-1),
                    IsActive = true,
                });
                context.TaxRates.Add(new TaxRate
                {
                    Jurisdiction = "TVQ (Québec)",
                    CountryCode = "CA",
                    RegionCode = "QC",
                    Rate = 0.09975m,
                    EffectiveFrom = DateTime.UtcNow.AddYears(-1),
                    IsActive = true,
                });
                context.SaveChanges();
            });
        }

        private static FormUrlEncodedContent CheckoutForm(int shippingMethodId, string state = "QC", IDictionary<string, string>? extraTamperFields = null)
        {
            var fields = new Dictionary<string, string>
            {
                ["Name"] = "Alice",
                ["PhoneNumber"] = "5145551234",
                ["StreetAddress"] = "1 rue Test",
                ["City"] = "Montreal",
                ["State"] = state,
                ["PostalCode"] = "H0H0H0",
                ["ShippingMethodId"] = shippingMethodId.ToString(),
            };

            if (extraTamperFields != null)
            {
                foreach (var kvp in extraTamperFields)
                {
                    fields[kvp.Key] = kvp.Value;
                }
            }

            return new FormUrlEncodedContent(fields);
        }

        [Fact]
        public async Task ValidCheckout_ComputesServerSideTotal_RedirectsToStripe()
        {
            var shippingMethodId = SeedShippingMethod(price: 15.00m);
            SeedTaxRates();
            var client = _factory.CreateTestClient().AsCustomerA();

            // ShoppingCartAId : produit à 25.00 CAD, quantité 2 (TestDataSeeder) -> subtotal 50.00.
            var response = await client.PostAsync("/Cart/Summary", CheckoutForm(shippingMethodId));

            Assert.Equal((HttpStatusCode)303, response.StatusCode);
            Assert.Equal(1, _factory.StripeCheckoutService.CallCount);

            var order = _factory.Query(ctx => ctx.OrderHeaders
                .OrderByDescending(o => o.Id)
                .First(o => o.ApplicationUserId == TestIdentities.CustomerAId && o.SessionId == _factory.StripeCheckoutService.SessionIdToReturn));

            Assert.Equal(50.00m, order.Subtotal);
            Assert.Equal(15.00m, order.ShippingAmount);
            // 50.00 * (0.05 + 0.09975) = 50.00 * 0.14975 = 7.4875 -> 2.50 + 4.99 = 7.49
            Assert.Equal(7.49m, order.TaxAmount);
            Assert.Equal(0m, order.DiscountAmount);
            Assert.Equal(order.Subtotal + order.ShippingAmount + order.TaxAmount, order.OrderTotal);
        }

        [Fact]
        public async Task TamperedFinancialAndStateFields_AreIgnored_OrderUsesServerComputedTotal()
        {
            var shippingMethodId = SeedShippingMethod(price: 15.00m);
            SeedTaxRates();
            var client = _factory.CreateTestClient().AsCustomerA();

            var tamperedForm = CheckoutForm(shippingMethodId, extraTamperFields: new Dictionary<string, string>
            {
                ["OrderTotal"] = "0.01",
                ["Subtotal"] = "0.01",
                ["ShippingAmount"] = "0",
                ["TaxAmount"] = "0",
                ["DiscountAmount"] = "999",
                ["PaymentStatus"] = "Approved",
                ["OrderStatus"] = "Processing",
                ["ApplicationUserId"] = TestIdentities.CustomerBId,
                ["SessionId"] = "cs_attacker_supplied",
            });

            var response = await client.PostAsync("/Cart/Summary", tamperedForm);

            Assert.Equal((HttpStatusCode)303, response.StatusCode);

            var order = _factory.Query(ctx => ctx.OrderHeaders
                .OrderByDescending(o => o.Id)
                .First(o => o.SessionId == _factory.StripeCheckoutService.SessionIdToReturn));

            // Les valeurs tentées par le client sont totalement absentes de CheckoutFormInput
            // : elles n'ont donc pu se lier nulle part. Le total reste celui calculé
            // serveur (50.00 + 15.00 + 7.49), jamais 0.01.
            Assert.Equal(50.00m, order.Subtotal);
            Assert.Equal(15.00m, order.ShippingAmount);
            Assert.Equal(7.49m, order.TaxAmount);
            Assert.Equal(0m, order.DiscountAmount);
            Assert.NotEqual(0.01m, order.OrderTotal);
            Assert.Equal(72.49m, order.OrderTotal);
            Assert.Equal(TestIdentities.CustomerAId, order.ApplicationUserId);
            Assert.Equal(Cosmechic.Utility.SD.PaymentStatusPending, order.PaymentStatus);
            Assert.Equal(Cosmechic.Utility.SD.StatusPending, order.OrderStatus);
        }

        [Fact]
        public async Task InvalidShippingMethodId_RejectsCheckout_NoOrderCreated_NoStripeCall()
        {
            SeedTaxRates();
            var client = _factory.CreateTestClient().AsCustomerA();
            var ordersBefore = _factory.Query(ctx => ctx.OrderHeaders.Count());

            var response = await client.PostAsync("/Cart/Summary", CheckoutForm(shippingMethodId: 9999));

            Assert.Equal(HttpStatusCode.Found, response.StatusCode); // redirection vers Index avec TempData["error"]
            Assert.Equal(0, _factory.StripeCheckoutService.CallCount);
            var ordersAfter = _factory.Query(ctx => ctx.OrderHeaders.Count());
            Assert.Equal(ordersBefore, ordersAfter);
        }

        [Fact]
        public async Task InactiveShippingMethod_RejectsCheckout_NoOrderCreated()
        {
            var shippingMethodId = SeedShippingMethod(price: 15.00m, isActive: false);
            SeedTaxRates();
            var client = _factory.CreateTestClient().AsCustomerA();
            var ordersBefore = _factory.Query(ctx => ctx.OrderHeaders.Count());

            var response = await client.PostAsync("/Cart/Summary", CheckoutForm(shippingMethodId));

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal(0, _factory.StripeCheckoutService.CallCount);
            var ordersAfter = _factory.Query(ctx => ctx.OrderHeaders.Count());
            Assert.Equal(ordersBefore, ordersAfter);
        }

        [Fact]
        public async Task AnonymousUser_CannotCheckout()
        {
            var shippingMethodId = SeedShippingMethod();
            var client = _factory.CreateTestClient().AsAnonymous();

            var response = await client.PostAsync("/Cart/Summary", CheckoutForm(shippingMethodId));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(0, _factory.StripeCheckoutService.CallCount);
        }

        public void Dispose() => _factory.Dispose();
    }
}
