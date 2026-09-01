using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cosmechic.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceProductCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "Produits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateCreation",
                table: "Produits",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<string>(
                name: "IngredientsInci",
                table: "Produits",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NetQuantity",
                table: "Produits",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeoDescription",
                table: "Produits",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeoTitle",
                table: "Produits",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                table: "Produits",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Produits",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsageInstructions",
                table: "Produits",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Warnings",
                table: "Produits",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Categories",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    BrandId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nom = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Disponible = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.BrandId);
                });

            migrationBuilder.CreateTable(
                name: "ProduitImages",
                columns: table => new
                {
                    ProduitImageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProduitId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProduitImages", x => x.ProduitImageId);
                    table.ForeignKey(
                        name: "FK_ProduitImages_Produits",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "ProduitID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Produits_BrandId",
                table: "Produits",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Produits_Disponible",
                table: "Produits",
                column: "Disponible");

            migrationBuilder.CreateIndex(
                name: "IX_Produits_Sku",
                table: "Produits",
                column: "Sku",
                unique: true,
                filter: "[Sku] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Produits_Slug",
                table: "Produits",
                column: "Slug",
                unique: true,
                filter: "[Slug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Slug",
                table: "Categories",
                column: "Slug",
                unique: true,
                filter: "[Slug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Brands_Nom",
                table: "Brands",
                column: "Nom",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Brands_Slug",
                table: "Brands",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProduitImages_ProduitId",
                table: "ProduitImages",
                column: "ProduitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Produits_Brands",
                table: "Produits",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "BrandId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Produits_Brands",
                table: "Produits");

            migrationBuilder.DropTable(
                name: "Brands");

            migrationBuilder.DropTable(
                name: "ProduitImages");

            migrationBuilder.DropIndex(
                name: "IX_Produits_BrandId",
                table: "Produits");

            migrationBuilder.DropIndex(
                name: "IX_Produits_Disponible",
                table: "Produits");

            migrationBuilder.DropIndex(
                name: "IX_Produits_Sku",
                table: "Produits");

            migrationBuilder.DropIndex(
                name: "IX_Produits_Slug",
                table: "Produits");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Slug",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "DateCreation",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "IngredientsInci",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "NetQuantity",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "SeoDescription",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "SeoTitle",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "Sku",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "UsageInstructions",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "Warnings",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Categories");
        }
    }
}
