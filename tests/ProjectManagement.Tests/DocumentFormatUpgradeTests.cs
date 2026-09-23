using Microsoft.EntityFrameworkCore;
using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Tests;

public class DocumentFormatUpgradeTests
{
    [Fact]
    public async Task ExistingDocumentsAndRevisionsKeepTheirExactPlainTextAfterUpgrade()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync("20260915185130_AddUserProfiles");
        int project;
        await using (var owner = database.Open())
            project = (await owner.Create.Handle(new("DOC", "Document review", ""), default)).Value;
        const string body = "# Kept as plain text\n  Leading and trailing spaces  \nŽurnalas";
        var saved = new DateTime(2026, 6, 18, 17, 31, 22, DateTimeKind.Utc);
        await using (var old = database.OpenContext())
        {
            await old.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ProjectDocuments (Id, ProjectId, Title, Body, CreatedById, UpdatedById, IsArchived, Version, CreatedAtUtc, UpdatedAtUtc) VALUES (1, {project}, 'Old notes', {body}, 'owner', 'owner', 0, 1, {saved}, {saved})");
            await old.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO DocumentRevisions (Id, ProjectId, DocumentId, Version, Title, Body, EditorId, SavedAtUtc) VALUES (1, {project}, 1, 1, 'Old notes', {body}, 'owner', {saved})");
        }

        // Act
        await using var upgraded = database.OpenContext();
        await upgraded.Database.MigrateAsync();
        var document = await upgraded.ProjectDocuments.SingleAsync();
        var revision = await upgraded.DocumentRevisions.SingleAsync();

        // Assert
        Assert.Equal(body, document.Body);
        Assert.Equal(body, revision.Body);
        Assert.Equal(DocumentFormat.PlainText, document.Format);
        Assert.Equal(DocumentFormat.PlainText, revision.Format);
        Assert.Equal(1, document.Version);
        Assert.Equal(saved, revision.SavedAtUtc);
        Assert.Equal("owner", revision.EditorId);
        Assert.Equal(3, await upgraded.Users.CountAsync());
    }
}
