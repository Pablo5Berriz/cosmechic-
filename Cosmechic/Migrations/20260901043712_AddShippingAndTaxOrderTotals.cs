using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cosmechic.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingAndTaxOrderTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "OrderHeaders",
                type: "money",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingAmount",
                table: "OrderHeaders",
                type: "money",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ShippingMethodId",
                table: "OrderHeaders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingMethodName",
                table: "OrderHeaders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                table: "OrderHeaders",
                type: "money",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "OrderHeaders",
                type: "money",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ShippingMethods",
                columns: table => new
                {
                    ShippingMethodId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Price = table.Column<decimal>(type: "money", nullable: false),
                    FreeShippingThreshold = table.Column<decimal>(type: "money", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EstimatedMinDays = table.Column<int>(type: "int", nullable: true),
                    EstimatedMaxDays = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingMethods", x => x.ShippingMethodId);
                    table.CheckConstraint("CK_ShippingMethods_FreeShippingThreshold_NonNegative", "[FreeShippingThreshold] IS NULL OR [FreeShippingThreshold] >= 0");
                    table.CheckConstraint("CK_ShippingMethods_Price_NonNegative", "[Price] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "TaxRates",
                columns: table => new
                {
                    TaxRateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Jurisdiction = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    RegionCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Rate = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRates", x => x.TaxRateId);
                    table.CheckConstraint("CK_TaxRates_Rate_NonNegative", "[Rate] >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderHeaders_ShippingMethodId",
                table: "OrderHeaders",
                column: "ShippingMethodId");

            // COSMECHIC-COMMERCE-OPERATIONS-001A (section 45/46) : stratégie de migration
            // historique — les colonnes ajoutées ci-dessus valent 0 par défaut pour toutes
            // les lignes existantes. Pour ces commandes déjà passées avant ce lot, on
            // reconstitue Subtotal = OrderTotal existant (Shipping/Tax/Discount restent à 0,
            // seule information disponible : on ne peut pas reconstituer rétroactivement
            // une ventilation qui n'a jamais été calculée). OrderTotal lui-même n'est jamais
            // modifié : aucune commande historique ne change de montant facturé. Doit
            // s'exécuter avant l'ajout de CK_OrderHeaders_Total_Equals_Components ci-dessous,
            // sans quoi la contrainte échouerait immédiatement sur toute ligne historique.
            migrationBuilder.Sql("UPDATE [OrderHeaders] SET [Subtotal] = [OrderTotal];");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderHeaders_DiscountAmount_NonNegative",
                table: "OrderHeaders",
                sql: "[DiscountAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderHeaders_ShippingAmount_NonNegative",
                table: "OrderHeaders",
                sql: "[ShippingAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderHeaders_Subtotal_NonNegative",
                table: "OrderHeaders",
                sql: "[Subtotal] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderHeaders_TaxAmount_NonNegative",
                table: "OrderHeaders",
                sql: "[TaxAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderHeaders_Total_Equals_Components",
                table: "OrderHeaders",
                sql: "[OrderTotal] = [Subtotal] + [ShippingAmount] + [TaxAmount] - [DiscountAmount]");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRates_Jurisdiction",
                table: "TaxRates",
                columns: new[] { "CountryCode", "RegionCode", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_OrderHeaders_ShippingMethods",
                table: "OrderHeaders",
                column: "ShippingMethodId",
                principalTable: "ShippingMethods",
                principalColumn: "ShippingMethodId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderHeaders_ShippingMethods",
                table: "OrderHeaders");

            migrationBuilder.DropTable(
                name: "ShippingMethods");

            migrationBuilder.DropTable(
                name: "TaxRates");

            migrationBuilder.DropIndex(
                name: "IX_OrderHeaders_ShippingMethodId",
                table: "OrderHeaders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderHeaders_DiscountAmount_NonNegative",
                table: "OrderHeaders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderHeaders_ShippingAmount_NonNegative",
                table: "OrderHeaders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderHeaders_Subtotal_NonNegative",
                table: "OrderHeaders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderHeaders_TaxAmount_NonNegative",
                table: "OrderHeaders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderHeaders_Total_Equals_Components",
                table: "OrderHeaders");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "OrderHeaders");

            migrationBuilder.DropColumn(
                name: "ShippingAmount",
                table: "OrderHeaders");

            migrationBuilder.DropColumn(
                name: "ShippingMethodId",
                table: "OrderHeaders");

            migrationBuilder.DropColumn(
                name: "ShippingMethodName",
                table: "OrderHeaders");

            migrationBuilder.DropColumn(
                name: "Subtotal",
                table: "OrderHeaders");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "OrderHeaders");
        }
    }
}
