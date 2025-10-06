using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueprintProWeb.Migrations
{
    /// <inheritdoc />
    public partial class ProjectTrackerUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "projectTrack_FinalizationNotes",
                table: "ProjectTrackers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Estimated Cost (construction materials):\n\n--\n\n" +
                              "Total Payment (payment for architect):\n\n--\n\n" +
                              "Other Information:\n\n--"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
            name: "projectTrack_FinalizationNotes",
            table: "ProjectTrackers");
        }
    }
}
