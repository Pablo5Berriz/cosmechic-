using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cosmechic.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnReasonCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 14) : defaultValue corrigé
            // manuellement de "" (généré par défaut par EF pour une colonne string NOT NULL)
            // vers "LegacyUnclassified" — "" n'est pas une valeur ReturnReasonCategory valide
            // et romprait la désérialisation de toute ligne ReturnItem historique existante
            // au premier chargement après cette migration. Ce défaut n'existe que pour cette
            // opération ADD COLUMN ponctuelle (backfill des lignes déjà en base) — jamais
            // reconduit comme configuration de modèle permanente (voir CosmechicsContext.cs :
            // aucun .HasDefaultValue() sur cette propriété, précisément pour éviter qu'EF ne
            // substitue silencieusement ce défaut à un ChangeOfMind explicite lors d'une
            // insertion future — ChangeOfMind vaut 0, le sentinel CLR par défaut de l'enum).
            // Ne classe jamais arbitrairement une ligne historique comme ChangeOfMind : sa
            // vraie catégorie d'origine n'est pas connue, LegacyUnclassified préserve cette
            // vérité plutôt que de l'inventer.
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ReturnItems",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "LegacyUnclassified");

            migrationBuilder.AddColumn<bool>(
                name: "CustomerDeclaredResellable",
                table: "ReturnItems",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOpened",
                table: "ReturnItems",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsUsed",
                table: "ReturnItems",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "ReturnItems");

            migrationBuilder.DropColumn(
                name: "CustomerDeclaredResellable",
                table: "ReturnItems");

            migrationBuilder.DropColumn(
                name: "IsOpened",
                table: "ReturnItems");

            migrationBuilder.DropColumn(
                name: "IsUsed",
                table: "ReturnItems");
        }
    }
}
