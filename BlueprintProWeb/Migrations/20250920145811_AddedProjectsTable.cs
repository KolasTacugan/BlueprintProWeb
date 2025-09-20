using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueprintProWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddedProjectsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<DateTime>(
                name: "blueprintCreatedDate",
                table: "Blueprints",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    project_Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    user_clientId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    user_architectId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    blueprint_Id = table.Column<int>(type: "int", nullable: false),
                    project_Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    project_Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    project_Budget = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    project_startDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    project_endDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.project_Id);
                    table.ForeignKey(
                        name: "FK_Projects_AspNetUsers_user_architectId",
                        column: x => x.user_architectId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Projects_AspNetUsers_user_clientId",
                        column: x => x.user_clientId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Projects_Blueprints_blueprint_Id",
                        column: x => x.blueprint_Id,
                        principalTable: "Blueprints",
                        principalColumn: "blueprintId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_blueprint_Id",
                table: "Projects",
                column: "blueprint_Id");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_user_architectId",
                table: "Projects",
                column: "user_architectId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_user_clientId",
                table: "Projects",
                column: "user_clientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropColumn(
                name: "blueprintCreatedDate",
                table: "Blueprints");
        }
    }
}
