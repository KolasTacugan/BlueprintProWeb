using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueprintProWeb.Migrations
{
    /// <inheritdoc />
    public partial class UserEmbedAi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PortfolioEmbedding",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PortfolioText",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PortfolioEmbedding",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PortfolioText",
                table: "AspNetUsers");
        }
    }
}
