using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavedFilters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Expression = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Ordering = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedFilters", x => x.Id);
                    table.CheckConstraint("CK_SavedFilter_Expression", "length(Expression) <= 2000");
                    table.CheckConstraint("CK_SavedFilter_Name", "length(trim(Name)) BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_SavedFilter_NormalizedName", "length(NormalizedName) BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_SavedFilter_Ordering", "length(Ordering) <= 200");
                    table.CheckConstraint("CK_SavedFilters_Version", "Version > 0");
                    table.ForeignKey(
                        name: "FK_SavedFilters_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavedFilters_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedFilters_OwnerId",
                table: "SavedFilters",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedFilters_ProjectId_OwnerId_NormalizedName",
                table: "SavedFilters",
                columns: new[] { "ProjectId", "OwnerId", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavedFilters");
        }
    }
}
