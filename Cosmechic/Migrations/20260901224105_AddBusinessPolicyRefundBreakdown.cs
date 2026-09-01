using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cosmechic.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessPolicyRefundBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cause",
                table: "Refunds",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MerchandiseAmount",
                table: "Refunds",
                type: "money",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingAmount",
                table: "Refunds",
                type: "money",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "Refunds",
                type: "money",
                nullable: false,
                defaultValue: 0m);

            // COSMECHIC-BUSINESS-POLICY-001 (section 12) : rétro-remplissage obligatoire —
            // toute ligne Refund historique a Amount > 0 (CK_Refunds_Amount_Positive) alors
            // que les 3 nouvelles colonnes valent 0 par défaut. Sans ce rétro-remplissage,
            // CK_Refunds_Breakdown_Equals_Amount ci-dessous échouerait immédiatement contre
            // toute base contenant déjà des remboursements (SQL Server valide les lignes
            // existantes à l'ajout d'une CHECK CONSTRAINT). Cohérent avec la même décision
            // prise côté code pour RequestRefundAsync (chemin manuel, non classifié) :
            // tout le montant historique est porté par MerchandiseAmount, Cause reste NULL.
            migrationBuilder.Sql("UPDATE [Refunds] SET [MerchandiseAmount] = [Amount] WHERE [MerchandiseAmount] = 0 AND [ShippingAmount] = 0 AND [TaxAmount] = 0;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Refunds_Breakdown_Equals_Amount",
                table: "Refunds",
                sql: "[MerchandiseAmount] + [ShippingAmount] + [TaxAmount] = [Amount]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Refunds_MerchandiseAmount_NonNegative",
                table: "Refunds",
                sql: "[MerchandiseAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Refunds_ShippingAmount_NonNegative",
                table: "Refunds",
                sql: "[ShippingAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Refunds_TaxAmount_NonNegative",
                table: "Refunds",
                sql: "[TaxAmount] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Refunds_Breakdown_Equals_Amount",
                table: "Refunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Refunds_MerchandiseAmount_NonNegative",
                table: "Refunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Refunds_ShippingAmount_NonNegative",
                table: "Refunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Refunds_TaxAmount_NonNegative",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "Cause",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "MerchandiseAmount",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "ShippingAmount",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "Refunds");
        }
    }
}
