using Cosmechic.Models;

namespace Cosmechic.Tests.Infrastructure
{
    // Jeu de données minimal et partagé : 2 clients (A, B), 1 admin, 1 catégorie/produit,
    // une commande + panier + avis appartenant à A. Sert de socle aux scénarios de la
    // matrice de tests de COSMECHIC-SECURITY-001 (section 16).
    public static class TestDataSeeder
    {
        public const int CategoryId = 1;
        public const int ProduitId = 1;
        public const int OrderHeaderAId = 1;
        public const int OrderHeaderBId = 2;
        public const int OrderDetailAId = 1;
        public const int ShoppingCartAId = 1;
        public const int ShoppingCartBId = 2;
        public const int AviAId = 1;
        public const int NonExistentId = 9999;

        public static void SeedStandardFixture(CustomWebApplicationFactory factory)
        {
            factory.Seed(context =>
            {
                context.AspNetUsers.Add(new AspNetUser
                {
                    Id = TestIdentities.CustomerAId,
                    UserName = "customer-a",
                    Email = "customer-a@example.test",
                });
                context.AspNetUsers.Add(new AspNetUser
                {
                    Id = TestIdentities.CustomerBId,
                    UserName = "customer-b",
                    Email = "customer-b@example.test",
                });
                context.AspNetUsers.Add(new AspNetUser
                {
                    Id = TestIdentities.AdminId,
                    UserName = "admin",
                    Email = "admin@example.test",
                });

                var category = new Category
                {
                    CategorieId = CategoryId,
                    Nom = "Soins visage",
                    Image = "categorie.jpg",
                    Disponible = true,
                };
                context.Categories.Add(category);

                var produit = new Produit
                {
                    ProduitId = ProduitId,
                    Nom = "Creme hydratante",
                    CategorieId = CategoryId,
                    Prix = 25.00m,
                    Stock = 10,
                    Disponible = true,
                    Image = "produit.jpg",
                    // Le fournisseur InMemory ne génère pas les colonnes rowversion
                    // automatiquement comme le ferait SQL Server ; valeur factice requise
                    // uniquement pour les tests (COSMECHIC-DATA-001).
                    RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
                };
                context.Produits.Add(produit);

                var orderHeaderA = new OrderHeader
                {
                    Id = OrderHeaderAId,
                    ApplicationUserId = TestIdentities.CustomerAId,
                    OrderDate = DateTime.UtcNow,
                    ShippingDate = DateTime.UtcNow,
                    OrderTotal = 25.00m,
                    Subtotal = 25.00m,
                    OrderStatus = "Pending",
                    PaymentStatus = "Pending",
                    SessionId = "cs_test_ordera",
                    PhoneNumber = "5145551234",
                    StreetAddress = "1 rue Test",
                    City = "Montreal",
                    State = "QC",
                    PostalCode = "H0H0H0",
                    Name = "customer-a",
                    PaymentDate = DateTime.UtcNow,
                    PaymentDueDate = DateTime.UtcNow,
                };
                context.OrderHeaders.Add(orderHeaderA);

                var orderHeaderB = new OrderHeader
                {
                    Id = OrderHeaderBId,
                    ApplicationUserId = TestIdentities.CustomerBId,
                    OrderDate = DateTime.UtcNow,
                    ShippingDate = DateTime.UtcNow,
                    OrderTotal = 25.00m,
                    Subtotal = 25.00m,
                    OrderStatus = "Pending",
                    PaymentStatus = "Pending",
                    SessionId = "cs_test_orderb",
                    PhoneNumber = "5145555678",
                    StreetAddress = "2 rue Test",
                    City = "Montreal",
                    State = "QC",
                    PostalCode = "H1H1H1",
                    Name = "customer-b",
                    PaymentDate = DateTime.UtcNow,
                    PaymentDueDate = DateTime.UtcNow,
                };
                context.OrderHeaders.Add(orderHeaderB);

                context.OrderDetails.Add(new OrderDetail
                {
                    Id = OrderDetailAId,
                    OrderHeaderId = OrderHeaderAId,
                    ProduitId = ProduitId,
                    Count = 1,
                    Price = 25.00m,
                });

                context.ShoppingCarts.Add(new ShoppingCart
                {
                    Id = ShoppingCartAId,
                    ApplicationUserId = TestIdentities.CustomerAId,
                    ProduitId = ProduitId,
                    Count = 2,
                });

                context.ShoppingCarts.Add(new ShoppingCart
                {
                    Id = ShoppingCartBId,
                    ApplicationUserId = TestIdentities.CustomerBId,
                    ProduitId = ProduitId,
                    Count = 1,
                });

                context.Avis.Add(new Avi
                {
                    ReviewId = AviAId,
                    AspNetUserId = TestIdentities.CustomerAId,
                    ProduitId = ProduitId,
                    Note = 5,
                    Commentaire = "Tres bon produit",
                    DateReview = DateTime.UtcNow.Date,
                });
            });
        }
    }
}
