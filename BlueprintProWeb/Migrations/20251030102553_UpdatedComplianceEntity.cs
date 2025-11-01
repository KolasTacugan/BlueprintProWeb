using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueprintProWeb.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedComplianceEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "compliance_Electrical",
                table: "Compliances");

            migrationBuilder.DropColumn(
                name: "compliance_Sanitary",
                table: "Compliances");

            migrationBuilder.DropColumn(
                name: "compliance_Structural",
                table: "Compliances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "compliance_Electrical",
                table: "Compliances",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "compliance_Sanitary",
                table: "Compliances",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "compliance_Structural",
                table: "Compliances",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
