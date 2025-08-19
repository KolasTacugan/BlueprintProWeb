using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueprintProWeb.Migrations
{
    /// <inheritdoc />
    public partial class clienttouser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LastName",
                table: "AspNetUsers",
                newName: "user_lname");

            migrationBuilder.RenameColumn(
                name: "FirstName",
                table: "AspNetUsers",
                newName: "user_fname");

            migrationBuilder.AddColumn<decimal>(
                name: "user_Budget",
                table: "AspNetUsers",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_Location",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "user_Rating",
                table: "AspNetUsers",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_Specialization",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_Style",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "user_createdDate",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "user_licenseNo",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_profilePhoto",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "user_Budget",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "user_Location",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "user_Rating",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "user_Specialization",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "user_Style",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "user_createdDate",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "user_licenseNo",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "user_profilePhoto",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "user_lname",
                table: "AspNetUsers",
                newName: "LastName");

            migrationBuilder.RenameColumn(
                name: "user_fname",
                table: "AspNetUsers",
                newName: "FirstName");
        }
    }
}
