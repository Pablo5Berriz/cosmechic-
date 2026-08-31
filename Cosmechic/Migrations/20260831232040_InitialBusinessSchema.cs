using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cosmechic.Migrations
{
    /// <inheritdoc />
    public partial class InitialBusinessSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlogPosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Titre = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Contenu = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Image = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DatePublication = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__BlogPost__3214EC07806A658A", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategorieID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nom = table.Column<string>(type: "nchar(450)", fixedLength: true, maxLength: 450, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Image = table.Column<string>(type: "nchar(450)", fixedLength: true, maxLength: 450, nullable: false),
                    Disponible = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.CategorieID);
                });

            migrationBuilder.CreateTable(
                name: "OrderHeaders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ShippingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrderTotal = table.Column<decimal>(type: "money", nullable: false),
                    OrderStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TrackingNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Carrier = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentDueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentIntentId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StreetAddress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PostalCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderHeaders", x => x.Id);
                    table.CheckConstraint("CK_OrderHeaders_OrderTotal_NonNegative", "[OrderTotal] >= 0");
                    table.ForeignKey(
                        name: "FK_OrderHeaders_AspNetUsers",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Titre = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Remise = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DateDebut = table.Column<DateTime>(type: "datetime", nullable: false),
                    DateFin = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Promotio__3214EC071F180FF8", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Produits",
                columns: table => new
                {
                    ProduitID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nom = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CategorieID = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Prix = table.Column<decimal>(type: "money", nullable: false),
                    Stock = table.Column<decimal>(type: "decimal(18,0)", nullable: false),
                    Disponible = table.Column<bool>(type: "bit", nullable: false),
                    Image = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    NombreVentes = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Table_1", x => x.ProduitID);
                    table.CheckConstraint("CK_Produits_Prix_NonNegative", "[Prix] >= 0");
                    table.CheckConstraint("CK_Produits_Stock_NonNegative", "[Stock] >= 0");
                    table.ForeignKey(
                        name: "FK_Produits_Categories",
                        column: x => x.CategorieID,
                        principalTable: "Categories",
                        principalColumn: "CategorieID");
                });

            migrationBuilder.CreateTable(
                name: "ProcessedStripeEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StripeEventId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessingStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedStripeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessedStripeEvents_OrderHeaders",
                        column: x => x.OrderId,
                        principalTable: "OrderHeaders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Avis",
                columns: table => new
                {
                    ReviewID = table.Column<int>(type: "int", nullable: false),
                    AspNetUserID = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProduitID = table.Column<int>(type: "int", nullable: false),
                    PaiementID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Note = table.Column<int>(type: "int", nullable: false),
                    Commentaire = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateReview = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Avis", x => x.ReviewID);
                    table.ForeignKey(
                        name: "FK_Avis_AspNetUsers",
                        column: x => x.AspNetUserID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Avis_Produits",
                        column: x => x.ProduitID,
                        principalTable: "Produits",
                        principalColumn: "ProduitID");
                });

            migrationBuilder.CreateTable(
                name: "OrderDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderHeaderId = table.Column<int>(type: "int", nullable: false),
                    ProduitId = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "money", nullable: false),
                    ProduitNom = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderDetails", x => x.Id);
                    table.CheckConstraint("CK_OrderDetails_Count_Positive", "[Count] > 0");
                    table.CheckConstraint("CK_OrderDetails_Price_NonNegative", "[Price] >= 0");
                    table.ForeignKey(
                        name: "FK_OrderDetails_OrderHeaders",
                        column: x => x.OrderHeaderId,
                        principalTable: "OrderHeaders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderDetails_Produits",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "ProduitID");
                });

            migrationBuilder.CreateTable(
                name: "ShoppingCarts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProduitId = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingCarts", x => x.Id);
                    table.CheckConstraint("CK_ShoppingCarts_Count_Positive", "[Count] > 0");
                    table.ForeignKey(
                        name: "FK_ShoppingCarts_Produits_ProduitId",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "ProduitID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TemoignagesClients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nom = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Commentaire = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Note = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    ProduitId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Temoigna__3214EC07F49AF2DD", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemoignagesClients_Produits",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "ProduitID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Avis_AspNetUserID",
                table: "Avis",
                column: "AspNetUserID");

            migrationBuilder.CreateIndex(
                name: "IX_Avis_ProduitID",
                table: "Avis",
                column: "ProduitID");

            migrationBuilder.CreateIndex(
                name: "IX_OrderDetails_OrderHeaderId",
                table: "OrderDetails",
                column: "OrderHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderDetails_ProduitId",
                table: "OrderDetails",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHeaders_ApplicationUserId",
                table: "OrderHeaders",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedStripeEvents_OrderId",
                table: "ProcessedStripeEvents",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedStripeEvents_StripeEventId",
                table: "ProcessedStripeEvents",
                column: "StripeEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Produits_CategorieID",
                table: "Produits",
                column: "CategorieID");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingCarts_ApplicationUserId",
                table: "ShoppingCarts",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingCarts_ProduitId",
                table: "ShoppingCarts",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_TemoignagesClients_ProduitId",
                table: "TemoignagesClients",
                column: "ProduitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Avis");

            migrationBuilder.DropTable(
                name: "BlogPosts");

            migrationBuilder.DropTable(
                name: "OrderDetails");

            migrationBuilder.DropTable(
                name: "ProcessedStripeEvents");

            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropTable(
                name: "ShoppingCarts");

            migrationBuilder.DropTable(
                name: "TemoignagesClients");

            migrationBuilder.DropTable(
                name: "OrderHeaders");

            migrationBuilder.DropTable(
                name: "Produits");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
