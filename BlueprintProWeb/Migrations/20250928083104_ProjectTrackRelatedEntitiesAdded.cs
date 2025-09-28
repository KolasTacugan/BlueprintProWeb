using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueprintProWeb.Migrations
{
    /// <inheritdoc />
    public partial class ProjectTrackRelatedEntitiesAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectTrackers",
                columns: table => new
                {
                    projectTrack_Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    project_Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    project_Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    blueprint_Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    projectTrack_dueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    projectTrack_Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTrackers", x => x.projectTrack_Id);
                    table.ForeignKey(
                        name: "FK_ProjectTrackers_Projects_project_Id",
                        column: x => x.project_Id,
                        principalTable: "Projects",
                        principalColumn: "project_Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Compliances",
                columns: table => new
                {
                    compliance_Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    projectTrack_Id = table.Column<int>(type: "int", nullable: false),
                    compliance_Structural = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    compliance_Electrical = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    compliance_Sanitary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    compliance_Zoning = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    compliance_Others = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Compliances", x => x.compliance_Id);
                    table.ForeignKey(
                        name: "FK_Compliances_ProjectTrackers_projectTrack_Id",
                        column: x => x.projectTrack_Id,
                        principalTable: "ProjectTrackers",
                        principalColumn: "projectTrack_Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectFiles",
                columns: table => new
                {
                    projectFile_Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    project_Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    projectFile_fileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    projectFile_Path = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    projectFile_Version = table.Column<int>(type: "int", nullable: false),
                    projectFile_uploadedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProjectTrackerprojectTrack_Id = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectFiles", x => x.projectFile_Id);
                    table.ForeignKey(
                        name: "FK_ProjectFiles_ProjectTrackers_ProjectTrackerprojectTrack_Id",
                        column: x => x.ProjectTrackerprojectTrack_Id,
                        principalTable: "ProjectTrackers",
                        principalColumn: "projectTrack_Id");
                    table.ForeignKey(
                        name: "FK_ProjectFiles_Projects_project_Id",
                        column: x => x.project_Id,
                        principalTable: "Projects",
                        principalColumn: "project_Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Compliances_projectTrack_Id",
                table: "Compliances",
                column: "projectTrack_Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFiles_project_Id",
                table: "ProjectFiles",
                column: "project_Id");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFiles_ProjectTrackerprojectTrack_Id",
                table: "ProjectFiles",
                column: "ProjectTrackerprojectTrack_Id");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTrackers_project_Id",
                table: "ProjectTrackers",
                column: "project_Id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Compliances");

            migrationBuilder.DropTable(
                name: "ProjectFiles");

            migrationBuilder.DropTable(
                name: "ProjectTrackers");
        }
    }
}
