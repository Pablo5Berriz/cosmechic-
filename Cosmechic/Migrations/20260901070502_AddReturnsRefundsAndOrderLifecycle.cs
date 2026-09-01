using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cosmechic.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnsRefundsAndOrderLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "OrderHeaders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FulfillmentStatus",
                table: "OrderHeaders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "OrderHeaders",
                type: "money",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "OrderHeaders",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShippedAt",
                table: "OrderHeaders",
                type: "datetime2",
                nullable: true);

            // COSMECHIC-COMMERCE-OPERATIONS-001B (section 6/63) : remappage déterministe des
            // commandes historiques vers le nouveau modèle à cinq dimensions, sans jamais
            // fabriquer une preuve d'expédition/livraison/remboursement qui n'existe pas.
            //
            // 1) FulfillmentStatus est déduit de l'ANCIENNE sémantique d'OrderStatus (avant
            //    remappage ci-dessous), la seule information déjà disponible : l'ancien
            //    "Processing" signifiait "payé et en cours de préparation" (jamais expédiée
            //    dans l'ancien modèle — aucune commande n'est donc marquée Shipped/Delivered
            //    ici, faute de preuve) ; l'ancien "Cancelled" couvrait déjà un échec de
            //    paiement ou une annulation, jamais une expédition.
            migrationBuilder.Sql(@"
                UPDATE [OrderHeaders] SET [FulfillmentStatus] =
                    CASE
                        WHEN [OrderStatus] = N'Processing' THEN N'Processing'
                        WHEN [OrderStatus] = N'Cancelled' THEN N'Cancelled'
                        ELSE N'Unfulfilled'
                    END;
            ");

            // 2) OrderStatus : "Processing" (payé, en préparation) devient "Confirmed" dans
            //    le nouveau modèle où le suivi d'expédition vit désormais dans
            //    FulfillmentStatus (déjà backfillé ci-dessus) ; Pending/Cancelled inchangés.
            migrationBuilder.Sql(@"
                UPDATE [OrderHeaders] SET [OrderStatus] =
                    CASE
                        WHEN [OrderStatus] = N'Processing' THEN N'Confirmed'
                        ELSE [OrderStatus]
                    END;
            ");

            // 3) PaymentStatus : "Approved" -> "Paid", "Rejected" -> "Failed" ; "Pending"
            //    inchangé. PartiallyRefunded/Refunded ne sont jamais attribués
            //    rétroactivement : aucun Refund historique n'existe avant ce lot.
            migrationBuilder.Sql(@"
                UPDATE [OrderHeaders] SET [PaymentStatus] =
                    CASE
                        WHEN [PaymentStatus] = N'Approved' THEN N'Paid'
                        WHEN [PaymentStatus] = N'Rejected' THEN N'Failed'
                        ELSE [PaymentStatus]
                    END;
            ");

            migrationBuilder.CreateTable(
                name: "OrderStatusHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderStatusHistories_OrderHeaders",
                        column: x => x.OrderId,
                        principalTable: "OrderHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CustomerComment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AdminComment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnRequests_OrderHeaders",
                        column: x => x.OrderId,
                        principalTable: "OrderHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProduitId = table.Column<int>(type: "int", nullable: false),
                    QuantityDelta = table.Column<decimal>(type: "decimal(18,0)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    ReturnItemId = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.CheckConstraint("CK_StockMovements_QuantityDelta_NotZero", "[QuantityDelta] <> 0");
                    table.ForeignKey(
                        name: "FK_StockMovements_Produits",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "ProduitID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Refunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ReturnRequestId = table.Column<int>(type: "int", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StripeRefundId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Amount = table.Column<decimal>(type: "money", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => x.Id);
                    table.CheckConstraint("CK_Refunds_Amount_Positive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_Refunds_OrderHeaders",
                        column: x => x.OrderId,
                        principalTable: "OrderHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_ReturnRequests",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnRequestId = table.Column<int>(type: "int", nullable: false),
                    OrderDetailId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Restocked = table.Column<bool>(type: "bit", nullable: false),
                    RestockedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnItems", x => x.Id);
                    table.CheckConstraint("CK_ReturnItems_Quantity_Positive", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ReturnItems_OrderDetails",
                        column: x => x.OrderDetailId,
                        principalTable: "OrderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnItems_ReturnRequests",
                        column: x => x.ReturnRequestId,
                        principalTable: "ReturnRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderHeaders_RefundedAmount_WithinTotal",
                table: "OrderHeaders",
                sql: "[RefundedAmount] >= 0 AND [RefundedAmount] <= [OrderTotal]");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistories_OrderId",
                table: "OrderStatusHistories",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_IdempotencyKey",
                table: "Refunds",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_OrderId",
                table: "Refunds",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_ReturnRequestId",
                table: "Refunds",
                column: "ReturnRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_StripeRefundId",
                table: "Refunds",
                column: "StripeRefundId",
                unique: true,
                filter: "[StripeRefundId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnItems_OrderDetailId",
                table: "ReturnItems",
                column: "OrderDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnItems_ReturnRequestId",
                table: "ReturnItems",
                column: "ReturnRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_ApplicationUserId",
                table: "ReturnRequests",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_OrderId",
                table: "ReturnRequests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProduitId",
                table: "StockMovements",
                column: "ProduitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Réversion au mieux des remappages de données (section 62) : redonne les
            // anciennes valeurs pour les commandes non touchées par ce lot. "Completed"
            // (nouveau statut, jamais présent avant migration) n'a pas d'équivalent
            // ancien — laissé tel quel, cas hors du périmètre d'un rollback réaliste.
            migrationBuilder.Sql(@"
                UPDATE [OrderHeaders] SET [OrderStatus] =
                    CASE WHEN [OrderStatus] = N'Confirmed' THEN N'Processing' ELSE [OrderStatus] END;
            ");
            migrationBuilder.Sql(@"
                UPDATE [OrderHeaders] SET [PaymentStatus] =
                    CASE
                        WHEN [PaymentStatus] = N'Paid' THEN N'Approved'
                        WHEN [PaymentStatus] = N'Failed' THEN N'Rejected'
                        ELSE [PaymentStatus]
                    END;
            ");

            migrationBuilder.DropTable(
                name: "OrderStatusHistories");

            migrationBuilder.DropTable(
                name: "Refunds");

            migrationBuilder.DropTable(
                name: "ReturnItems");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "ReturnRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderHeaders_RefundedAmount_WithinTotal",
                table: "OrderHeaders");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "OrderHeaders");

            migrationBuilder.DropColumn(
                name: "FulfillmentStatus",
                table: "OrderHeaders");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "OrderHeaders");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "OrderHeaders");

            migrationBuilder.DropColumn(
                name: "ShippedAt",
                table: "OrderHeaders");
        }
    }
}
