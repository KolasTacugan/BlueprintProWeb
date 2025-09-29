using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueprintProWeb.Migrations
{
    /// <inheritdoc />
    public partial class ProjectTrackerUpdated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "projectTrack_currentFileName",
                table: "ProjectTrackers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "projectTrack_currentFilePath",
                table: "ProjectTrackers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "projectTrack_currentRevision",
                table: "ProjectTrackers",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "projectTrack_currentFileName",
                table: "ProjectTrackers");

            migrationBuilder.DropColumn(
                name: "projectTrack_currentFilePath",
                table: "ProjectTrackers");

            migrationBuilder.DropColumn(
                name: "projectTrack_currentRevision",
                table: "ProjectTrackers");
        }
    }
}
