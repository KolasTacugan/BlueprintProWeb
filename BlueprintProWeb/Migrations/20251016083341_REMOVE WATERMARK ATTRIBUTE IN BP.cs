using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueprintProWeb.Migrations
{
    /// <inheritdoc />
    public partial class REMOVEWATERMARKATTRIBUTEINBP : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "blueprintWatermarkedImage",
                table: "Blueprints");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "blueprintWatermarkedImage",
                table: "Blueprints",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
