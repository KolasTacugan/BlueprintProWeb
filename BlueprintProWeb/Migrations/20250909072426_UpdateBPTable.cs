using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueprintProWeb.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBPTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "blueprintIsForSale",
                table: "Blueprints",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "blueprintIsForSale",
                table: "Blueprints");
        }
    }
}
