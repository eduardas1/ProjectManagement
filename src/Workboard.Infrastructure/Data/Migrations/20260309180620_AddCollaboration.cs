using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workboard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCollaboration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentId",
                table: "ChangeEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorId = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedById = table.Column<string>(type: "TEXT", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModerationReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.CheckConstraint("CK_Comment_Reason", "(ModerationReason IS NULL OR length(ModerationReason) <= 500) AND (IsDeleted = 0 OR DeletedById = AuthorId OR length(trim(coalesce(ModerationReason, ''))) > 0)");
                    table.CheckConstraint("CK_Comment_Removal", "(IsDeleted = 0 AND length(trim(Body)) BETWEEN 1 AND 5000 AND DeletedById IS NULL AND DeletedAtUtc IS NULL AND ModerationReason IS NULL) OR (IsDeleted = 1 AND Body = '' AND DeletedById IS NOT NULL AND DeletedAtUtc IS NOT NULL)");
                    table.CheckConstraint("CK_Comment_Version", "Version > 0");
                    table.ForeignKey(
                        name: "FK_Comments_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comments_AspNetUsers_DeletedById",
                        column: x => x.DeletedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comments_WorkItems_ProjectId_WorkItemId",
                        columns: x => new { x.ProjectId, x.WorkItemId },
                        principalTable: "WorkItems",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    CreatedById = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedById = table.Column<string>(type: "TEXT", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDocuments", x => x.Id);
                    table.UniqueConstraint("AK_ProjectDocuments_ProjectId_Id", x => new { x.ProjectId, x.Id });
                    table.CheckConstraint("CK_Document_Body", "length(Body) <= 100000");
                    table.CheckConstraint("CK_Document_Title", "length(trim(Title)) BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_Document_Version", "Version > 0");
                    table.ForeignKey(
                        name: "FK_ProjectDocuments_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectDocuments_AspNetUsers_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectDocuments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: true),
                    StoredName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Length = table.Column<int>(type: "INTEGER", nullable: false),
                    Contents = table.Column<byte[]>(type: "BLOB", nullable: false),
                    UploadedById = table.Column<string>(type: "TEXT", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.CheckConstraint("CK_Attachment_Length", "Length BETWEEN 1 AND 5242880 AND typeof(Contents) = 'blob' AND length(Contents) = Length");
                    table.CheckConstraint("CK_Attachment_Name", "length(StoredName) BETWEEN 1 AND 64 AND length(DisplayName) BETWEEN 1 AND 255");
                    table.CheckConstraint("CK_Attachment_Target", "(WorkItemId IS NOT NULL AND DocumentId IS NULL) OR (WorkItemId IS NULL AND DocumentId IS NOT NULL)");
                    table.CheckConstraint("CK_Attachment_Type", "ContentType IN ('application/pdf', 'image/png', 'image/jpeg', 'text/plain', 'text/csv')");
                    table.CheckConstraint("CK_Attachment_Version", "Version > 0");
                    table.ForeignKey(
                        name: "FK_Attachments_AspNetUsers_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Attachments_ProjectDocuments_ProjectId_DocumentId",
                        columns: x => new { x.ProjectId, x.DocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Attachments_WorkItems_ProjectId_WorkItemId",
                        columns: x => new { x.ProjectId, x.WorkItemId },
                        principalTable: "WorkItems",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    EditorId = table.Column<string>(type: "TEXT", nullable: false),
                    SavedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRevisions", x => x.Id);
                    table.CheckConstraint("CK_Revision_Body", "length(Body) <= 100000");
                    table.CheckConstraint("CK_Revision_Title", "length(trim(Title)) BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_Revision_Version", "Version > 0");
                    table.ForeignKey(
                        name: "FK_DocumentRevisions_AspNetUsers_EditorId",
                        column: x => x.EditorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRevisions_ProjectDocuments_ProjectId_DocumentId",
                        columns: x => new { x.ProjectId, x.DocumentId },
                        principalTable: "ProjectDocuments",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEvents_ProjectId_DocumentId_OccurredAtUtc_Id",
                table: "ChangeEvents",
                columns: new[] { "ProjectId", "DocumentId", "OccurredAtUtc", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Change_Target",
                table: "ChangeEvents",
                sql: "WorkItemId IS NULL OR DocumentId IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_ProjectId_DocumentId",
                table: "Attachments",
                columns: new[] { "ProjectId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_ProjectId_WorkItemId",
                table: "Attachments",
                columns: new[] { "ProjectId", "WorkItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_StoredName",
                table: "Attachments",
                column: "StoredName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_UploadedById",
                table: "Attachments",
                column: "UploadedById");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_AuthorId",
                table: "Comments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_DeletedById",
                table: "Comments",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ProjectId_WorkItemId_CreatedAtUtc_Id",
                table: "Comments",
                columns: new[] { "ProjectId", "WorkItemId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_DocumentId_Version",
                table: "DocumentRevisions",
                columns: new[] { "DocumentId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_EditorId",
                table: "DocumentRevisions",
                column: "EditorId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_ProjectId_DocumentId",
                table: "DocumentRevisions",
                columns: new[] { "ProjectId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_CreatedById",
                table: "ProjectDocuments",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_UpdatedById",
                table: "ProjectDocuments",
                column: "UpdatedById");

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeEvents_ProjectDocuments_ProjectId_DocumentId",
                table: "ChangeEvents",
                columns: new[] { "ProjectId", "DocumentId" },
                principalTable: "ProjectDocuments",
                principalColumns: new[] { "ProjectId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChangeEvents_ProjectDocuments_ProjectId_DocumentId",
                table: "ChangeEvents");

            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "DocumentRevisions");

            migrationBuilder.DropTable(
                name: "ProjectDocuments");

            migrationBuilder.DropIndex(
                name: "IX_ChangeEvents_ProjectId_DocumentId_OccurredAtUtc_Id",
                table: "ChangeEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Change_Target",
                table: "ChangeEvents");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "ChangeEvents");
        }
    }
}
