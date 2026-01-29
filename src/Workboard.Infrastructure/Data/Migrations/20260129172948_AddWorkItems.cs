using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workboard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkItemId",
                table: "ChangeEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Labels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Labels", x => x.Id);
                    table.UniqueConstraint("AK_Labels_ProjectId_Id", x => new { x.ProjectId, x.Id });
                    table.CheckConstraint("CK_Label_Name", "length(trim(Name)) BETWEEN 1 AND 40");
                    table.CheckConstraint("CK_Label_NormalizedName", "length(NormalizedName) BETWEEN 1 AND 40");
                    table.CheckConstraint("CK_Label_Version", "Version > 0");
                    table.ForeignKey(
                        name: "FK_Labels_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<string>(type: "TEXT", nullable: false),
                    AssigneeId = table.Column<string>(type: "TEXT", nullable: true),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    EpicId = table.Column<int>(type: "INTEGER", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedById = table.Column<string>(type: "TEXT", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItems", x => x.Id);
                    table.UniqueConstraint("AK_WorkItems_ProjectId_Id", x => new { x.ProjectId, x.Id });
                    table.CheckConstraint("CK_Work_Description", "length(Description) <= 20000");
                    table.CheckConstraint("CK_Work_Hierarchy", "(Type = 'Subtask' AND ParentId IS NOT NULL AND EpicId IS NULL) OR (Type IN ('Bug', 'Task', 'Story') AND ParentId IS NULL) OR (Type = 'Epic' AND ParentId IS NULL AND EpicId IS NULL)");
                    table.CheckConstraint("CK_Work_Number", "Number > 0");
                    table.CheckConstraint("CK_Work_Priority", "Priority IN ('Low', 'Medium', 'High', 'Critical')");
                    table.CheckConstraint("CK_Work_Self", "(ParentId IS NULL OR ParentId <> Id) AND (EpicId IS NULL OR EpicId <> Id)");
                    table.CheckConstraint("CK_Work_Status", "Status IN ('Backlog', 'Todo', 'InProgress', 'InReview', 'Done', 'Cancelled')");
                    table.CheckConstraint("CK_Work_Title", "length(trim(Title)) BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_Work_Type", "Type IN ('Bug', 'Task', 'Story', 'Epic', 'Subtask')");
                    table.CheckConstraint("CK_Work_Version", "Version > 0");
                    table.ForeignKey(
                        name: "FK_WorkItems_AspNetUsers_AssigneeId",
                        column: x => x.AssigneeId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItems_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItems_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItems_WorkItems_ProjectId_EpicId",
                        columns: x => new { x.ProjectId, x.EpicId },
                        principalTable: "WorkItems",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItems_WorkItems_ProjectId_ParentId",
                        columns: x => new { x.ProjectId, x.ParentId },
                        principalTable: "WorkItems",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemLabels",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    LabelId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemLabels", x => new { x.ProjectId, x.WorkItemId, x.LabelId });
                    table.ForeignKey(
                        name: "FK_WorkItemLabels_Labels_ProjectId_LabelId",
                        columns: x => new { x.ProjectId, x.LabelId },
                        principalTable: "Labels",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemLabels_WorkItems_ProjectId_WorkItemId",
                        columns: x => new { x.ProjectId, x.WorkItemId },
                        principalTable: "WorkItems",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedById = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemLinks", x => x.Id);
                    table.CheckConstraint("CK_Link_Kind", "Kind IN ('Blocks', 'Duplicates')");
                    table.CheckConstraint("CK_Link_Self", "SourceId <> TargetId");
                    table.ForeignKey(
                        name: "FK_WorkItemLinks_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemLinks_WorkItems_ProjectId_SourceId",
                        columns: x => new { x.ProjectId, x.SourceId },
                        principalTable: "WorkItems",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemLinks_WorkItems_ProjectId_TargetId",
                        columns: x => new { x.ProjectId, x.TargetId },
                        principalTable: "WorkItems",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEvents_ProjectId_WorkItemId_OccurredAtUtc_Id",
                table: "ChangeEvents",
                columns: new[] { "ProjectId", "WorkItemId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Labels_ProjectId_NormalizedName",
                table: "Labels",
                columns: new[] { "ProjectId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemLabels_ProjectId_LabelId_WorkItemId",
                table: "WorkItemLabels",
                columns: new[] { "ProjectId", "LabelId", "WorkItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemLinks_CreatedById",
                table: "WorkItemLinks",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemLinks_ProjectId_SourceId",
                table: "WorkItemLinks",
                columns: new[] { "ProjectId", "SourceId" },
                unique: true,
                filter: "Kind = 'Duplicates'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemLinks_ProjectId_SourceId_TargetId_Kind",
                table: "WorkItemLinks",
                columns: new[] { "ProjectId", "SourceId", "TargetId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemLinks_ProjectId_TargetId",
                table: "WorkItemLinks",
                columns: new[] { "ProjectId", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_AssigneeId",
                table: "WorkItems",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_CreatedById",
                table: "WorkItems",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_ProjectId_EpicId",
                table: "WorkItems",
                columns: new[] { "ProjectId", "EpicId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_ProjectId_IsArchived_AssigneeId_Id",
                table: "WorkItems",
                columns: new[] { "ProjectId", "IsArchived", "AssigneeId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_ProjectId_IsArchived_DueDate_Id",
                table: "WorkItems",
                columns: new[] { "ProjectId", "IsArchived", "DueDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_ProjectId_IsArchived_Status_Id",
                table: "WorkItems",
                columns: new[] { "ProjectId", "IsArchived", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_ProjectId_Number",
                table: "WorkItems",
                columns: new[] { "ProjectId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_ProjectId_ParentId",
                table: "WorkItems",
                columns: new[] { "ProjectId", "ParentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeEvents_WorkItems_ProjectId_WorkItemId",
                table: "ChangeEvents",
                columns: new[] { "ProjectId", "WorkItemId" },
                principalTable: "WorkItems",
                principalColumns: new[] { "ProjectId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChangeEvents_WorkItems_ProjectId_WorkItemId",
                table: "ChangeEvents");

            migrationBuilder.DropTable(
                name: "WorkItemLabels");

            migrationBuilder.DropTable(
                name: "WorkItemLinks");

            migrationBuilder.DropTable(
                name: "Labels");

            migrationBuilder.DropTable(
                name: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_ChangeEvents_ProjectId_WorkItemId_OccurredAtUtc_Id",
                table: "ChangeEvents");

            migrationBuilder.DropColumn(
                name: "WorkItemId",
                table: "ChangeEvents");
        }
    }
}
