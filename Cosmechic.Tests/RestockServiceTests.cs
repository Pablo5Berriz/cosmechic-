using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Tests.Infrastructure;
using Cosmechic.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cosmechic.Tests
{
    public class RestockServiceTests : IDisposable
    {
        private readonly CosmechicsContext _context = InMemoryContextFactory.Create();
        private readonly RestockService _sut;

        public RestockServiceTests()
        {
            _sut = new RestockService(_context, NullLogger<RestockService>.Instance);
        }

        private (Produit Produit, ReturnItem ReturnItem) SeedReceivedReturn(decimal stock = 5, int quantity = 2, string returnStatus = "Received")
        {
            var categorie = new Category { Nom = "Cat", Image = "c.jpg", Disponible = true };
            _context.Categories.Add(categorie);
            _context.SaveChanges();

            var produit = new Produit { Nom = "A", CategorieId = categorie.CategorieId, Prix = 10m, Stock = stock, Disponible = true, Image = "a.jpg", RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 } };
            _context.Produits.Add(produit);
            _context.SaveChanges();

            var order = new OrderHeader
            {
                ApplicationUserId = "user-a",
                OrderDate = DateTime.UtcNow,
                OrderTotal = 20m,
                Subtotal = 20m,
                OrderStatus = SD.OrderStatusConfirmed,
                PaymentStatus = SD.PaymentStatusPaid,
                FulfillmentStatus = SD.FulfillmentStatusShipped,
                Name = "Test",
                PhoneNumber = "5145551234",
                StreetAddress = "1 rue Test",
                City = "Montreal",
                State = "QC",
                PostalCode = "H0H0H0",
            };
            _context.OrderHeaders.Add(order);
            _context.SaveChanges();

            var detail = new OrderDetail { OrderHeaderId = order.Id, ProduitId = produit.ProduitId, Count = quantity, Price = 10m, ProduitNom = "A" };
            _context.OrderDetails.Add(detail);
            _context.SaveChanges();

            var returnRequest = new ReturnRequest
            {
                OrderId = order.Id,
                ApplicationUserId = "user-a",
                Status = returnStatus,
                CreatedAt = DateTime.UtcNow,
            };
            _context.ReturnRequests.Add(returnRequest);
            _context.SaveChanges();

            var returnItem = new ReturnItem { ReturnRequestId = returnRequest.Id, OrderDetailId = detail.Id, Quantity = quantity };
            _context.ReturnItems.Add(returnItem);
            _context.SaveChanges();

            return (produit, returnItem);
        }

        [Fact]
        public async Task ReceivedReturn_CompleteRestock_IncrementsStock()
        {
            var (produit, returnItem) = SeedReceivedReturn(stock: 5, quantity: 2);

            var result = await _sut.CompleteRestockAsync(returnItem.Id, "admin-1");

            Assert.IsType<RestockCompleted>(result);
            var reloaded = _context.Produits.AsNoTracking().Single(p => p.ProduitId == produit.ProduitId);
            Assert.Equal(7, reloaded.Stock);

            var movement = _context.StockMovements.AsNoTracking().Single(m => m.ProduitId == produit.ProduitId);
            Assert.Equal(2, movement.QuantityDelta);
            Assert.Equal(SD.StockMovementReasonReturnRestock, movement.Reason);
        }

        [Fact]
        public async Task DoubleRestock_SecondCallIsNoOp_StockIncrementedOnce()
        {
            var (produit, returnItem) = SeedReceivedReturn(stock: 5, quantity: 2);

            var first = await _sut.CompleteRestockAsync(returnItem.Id, "admin-1");
            var second = await _sut.CompleteRestockAsync(returnItem.Id, "admin-1");

            Assert.IsType<RestockCompleted>(first);
            Assert.IsType<RestockAlreadyDone>(second);

            var reloaded = _context.Produits.AsNoTracking().Single(p => p.ProduitId == produit.ProduitId);
            Assert.Equal(7, reloaded.Stock);
            Assert.Single(_context.StockMovements.Where(m => m.ProduitId == produit.ProduitId));
        }

        [Fact]
        public async Task NotYetReceived_RestockIsRejected()
        {
            var (_, returnItem) = SeedReceivedReturn(returnStatus: "Approved");

            var result = await _sut.CompleteRestockAsync(returnItem.Id, "admin-1");

            Assert.IsType<RestockRejected>(result);
        }

        public void Dispose() => _context.Dispose();
    }
}
