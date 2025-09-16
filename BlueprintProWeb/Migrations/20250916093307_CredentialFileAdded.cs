using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueprintProWeb.Migrations
{
    /// <inheritdoc />
    public partial class CredentialFileAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "user_CredentialsFile",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropColumn(
                name: "user_CredentialsFile",
                table: "AspNetUsers");
        }
    }
}
