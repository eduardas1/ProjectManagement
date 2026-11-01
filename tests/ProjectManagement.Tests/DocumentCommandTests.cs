using ProjectManagement.Domain.Enums;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Documents;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Infrastructure.Repositories;

namespace ProjectManagement.Tests;

public class DocumentCommandTests
{
    [Fact]
    public async Task FormatChangesKeepExactSourceAndEachRevisionsInterpretation()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        const string source = "# Original text\n  with exact spaces  \n";
        int id;
        await using (var owner = new DocumentSession(database.OpenContext(), new TestUser("owner")))
            id = (await owner.CreateDocument.Handle(new(project, "Notes", source), default)).Value;

        // Act
        await using (var owner = new DocumentSession(database.OpenContext(), new TestUser("owner")))
        {
            Assert.Equal(OperationStatus.Success, (await owner.UpdateDocument.Handle(new(project, id, 1, "Notes", source, DocumentFormat.Markdown), default)).Status);
            Assert.Equal(OperationStatus.Invalid, (await owner.UpdateDocument.Handle(new(project, id, 2, "Notes", source, (DocumentFormat)99), default)).Status);
        }
        await using var read = database.OpenContext();
        var revisions = await read.DocumentRevisions.OrderBy(revision => revision.Version).ToListAsync();
        var reader = new CollaborationReadRepository(read, new TestUser("owner"));
        var document = await reader.GetDocumentAsync(project, id, default);
        var original = await reader.GetRevisionAsync(project, id, revisions[0].Id, default);

        // Assert
        Assert.Equal(2, document!.Document.Version);
        Assert.Equal(DocumentFormat.Markdown, document.Format);
        Assert.Equal(DocumentFormat.PlainText, original!.Format);
        Assert.Equal(source, original.Body);
        Assert.Equal(source, document.Body);
        Assert.Equal(new[] { DocumentFormat.PlainText, DocumentFormat.Markdown }, revisions.Select(revision => revision.Format));
        Assert.All(revisions, revision => Assert.Equal(source, revision.Body));
    }

    [Fact]
    public async Task SharedEditsKeepImmutableSnapshotsAcrossExistingVersionGaps()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        int id;
        await using (var owner = new DocumentSession(database.OpenContext(), new TestUser("owner")))
            id = (await owner.CreateDocument.Handle(new(project, " Plan ", null), default)).Value;
        const string body = "  Exact text\nwith trailing spaces  ";
        await using (var member = new DocumentSession(database.OpenContext(), new TestUser("member")))
            Assert.Equal(OperationStatus.Success, (await member.UpdateDocument.Handle(new(project, id, 1, "Plan", body), default)).Status);

        // Act
        await using (var fixture = database.OpenContext())
        {
            (await fixture.ProjectDocuments.SingleAsync()).Version = 4;
            await fixture.SaveChangesAsync();
        }
        await using (var member = new DocumentSession(database.OpenContext(), new TestUser("member")))
        {
            Assert.Equal(OperationStatus.Success, (await member.UpdateDocument.Handle(new(project, id, 4, "Final plan", "Final body"), default)).Status);
            Assert.Equal(OperationStatus.Success, (await member.UpdateDocument.Handle(new(project, id, 5, " Final plan ", "Final body"), default)).Status);
            Assert.Equal(OperationStatus.Conflict, (await member.UpdateDocument.Handle(new(project, id, 4, "Final plan", "Final body"), default)).Status);
        }
        await using var read = database.OpenContext();
        var document = await read.ProjectDocuments.SingleAsync();
        var revisions = await read.DocumentRevisions.OrderBy(revision => revision.Version).ToListAsync();
        var events = await read.ChangeEvents.Where(change => change.DocumentId == id).OrderBy(change => change.Id).ToListAsync();

        // Assert
        Assert.Equal("owner", document.CreatedById);
        Assert.Equal("member", document.UpdatedById);
        Assert.Equal(5, document.Version);
        Assert.False(document.IsArchived);
        Assert.Equal("Final plan", document.Title);
        Assert.Equal("Final body", document.Body);
        Assert.Equal(new long[] { 1, 2, 5 }, revisions.Select(revision => revision.Version));
        Assert.Equal(new[] { "", body, "Final body" }, revisions.Select(revision => revision.Body));
        Assert.Equal(new[] { "owner", "member", "member" }, revisions.Select(revision => revision.EditorId));
        Assert.All(revisions, revision => Assert.Equal(DateTimeKind.Utc, revision.SavedAtUtc.Kind));
        Assert.Equal(new[] { "DocumentCreated", "DocumentUpdated", "DocumentUpdated" }, events.Select(change => change.Action));
        Assert.All(events, change =>
        {
            Assert.Null(change.WorkItemId);
            Assert.DoesNotContain("body", change.Summary + change.DetailsJson, StringComparison.OrdinalIgnoreCase);
            using var details = JsonDocument.Parse(change.DetailsJson);
            Assert.Single(details.RootElement.EnumerateObject());
            Assert.True(details.RootElement.GetProperty("Version").GetInt64() > 0);
        });
        Assert.Equal(2, (await read.Projects.SingleAsync()).Version);
        Assert.Equal(1, (await read.Projects.SingleAsync()).NextWorkItemNumber);
    }

    [Fact]
    public async Task SharedDocumentRightsFollowCurrentMembershipAfterTransfer()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        var id = await CreateAsync(database, project, "Plan");
        await using (var owner = database.Open())
        {
            await owner.AddMember.Handle(new(project, 2, "other@example.test"), default);
            await owner.Transfer.Handle(new(project, 3, "other"), default);
        }

        // Act
        await using (var former = new DocumentSession(database.OpenContext(), new TestUser("owner")))
            Assert.Equal(OperationStatus.Success, (await former.UpdateDocument.Handle(new(project, id, 1, "Shared", "Member edit"), default)).Status);
        await using (var current = new ProjectSession(database.OpenContext(), new TestUser("other")))
            Assert.Equal(OperationStatus.Success, (await current.RemoveMember.Handle(new(project, 4, "owner"), default)).Status);
        await using var revoked = new DocumentSession(database.OpenContext(), new TestUser("owner"));
        var edit = await revoked.UpdateDocument.Handle(new(project, id, 2, "Revoked", ""), default);
        var create = await revoked.CreateDocument.Handle(new(project, "Revoked", null), default);
        await using var read = database.OpenContext();
        var document = await read.ProjectDocuments.SingleAsync();

        // Assert
        Assert.Equal(OperationStatus.NotFound, edit.Status);
        Assert.Equal(OperationStatus.NotFound, create.Status);
        Assert.Equal("Shared", document.Title);
        Assert.Equal(2, document.Version);
        Assert.Equal(new long[] { 1, 2 }, await read.DocumentRevisions.OrderBy(revision => revision.Version).Select(revision => revision.Version).ToArrayAsync());
    }

    [Fact]
    public async Task BoundsForeignTargetsAndLegacyRetiredProjectDoNotChangeContent()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        var id = await CreateAsync(database, project, "Plan");
        int foreign;
        await using (var owner = database.Open()) foreign = (await owner.Create.Handle(new("CD", "Other", ""), default)).Value;
        var foreignDocument = await CreateAsync(database, foreign, "Foreign");
        await using var member = new DocumentSession(database.OpenContext(), new TestUser("member"));

        // Act
        Assert.Equal(OperationStatus.Invalid, (await member.CreateDocument.Handle(new(project, " ", null), default)).Status);
        Assert.Equal(OperationStatus.Invalid, (await member.CreateDocument.Handle(new(project, new string('x', 201), null), default)).Status);
        Assert.Equal(OperationStatus.Invalid, (await member.UpdateDocument.Handle(new(project, id, 1, "Mutated", new string('x', 100001)), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await member.UpdateDocument.Handle(new(project, foreignDocument, 1, "Wrong parent", ""), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await member.CreateDocument.Handle(new(foreign, "Private", null), default)).Status);
        Assert.Equal(OperationStatus.Success, (await member.CreateDocument.Handle(new(project, new string('x', 200), new string('b', 100000)), default)).Status);
        await using (var fixture = database.OpenContext())
        {
            (await fixture.Projects.SingleAsync(item => item.Id == project)).IsArchived = true;
            await fixture.SaveChangesAsync();
        }
        await using (var current = new DocumentSession(database.OpenContext(), new TestUser("member")))
        {
            Assert.Equal(OperationStatus.NotFound, (await current.UpdateDocument.Handle(new(project, id, 1, "Archived", null), default)).Status);
            Assert.Equal(OperationStatus.NotFound, (await current.CreateDocument.Handle(new(project, "Archived", null), default)).Status);
        }
        await using var read = database.OpenContext();
        var document = await read.ProjectDocuments.SingleAsync(document => document.Id == id);

        // Assert
        Assert.Equal("Plan", document.Title);
        Assert.Equal("", document.Body);
        Assert.Equal(1, document.Version);
        Assert.Single(await read.DocumentRevisions.Where(revision => revision.DocumentId == id).ToListAsync());
        Assert.Single(await read.ChangeEvents.Where(change => change.DocumentId == id).ToListAsync());
        Assert.Equal(3, await read.ProjectDocuments.CountAsync());
    }

    [Fact]
    public async Task FailedEventRollsBackDocumentSnapshotsAndEdits()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);

        // Act
        await using (var failing = new DocumentSession(database.OpenContext(new RejectProjectEvent()), new TestUser("member")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.CreateDocument.Handle(new(project, "Not saved", "Private body"), default));
        await using (var read = database.OpenContext())
        {
            Assert.Empty(await read.ProjectDocuments.ToListAsync());
            Assert.Empty(await read.DocumentRevisions.ToListAsync());
        }
        var id = await CreateAsync(database, project, "Original");
        await using (var failing = new DocumentSession(database.OpenContext(new RejectProjectEvent()), new TestUser("member")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.UpdateDocument.Handle(new(project, id, 1, "Not saved", "Private body"), default));
        await using var context = database.OpenContext();
        var document = await context.ProjectDocuments.SingleAsync();

        // Assert
        Assert.Equal("Original", document.Title);
        Assert.Equal("", document.Body);
        Assert.Equal(1, document.Version);
        Assert.False(document.IsArchived);
        Assert.Single(await context.DocumentRevisions.ToListAsync());
        Assert.Single(await context.ChangeEvents.Where(change => change.DocumentId == id).ToListAsync());
    }

    [Fact]
    public async Task ScopedMetadataAndRevisionPagesRetainCompleteBodiesOnlyInDetail()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        int id;
        await using (var owner = new DocumentSession(database.OpenContext(), new TestUser("owner")))
        {
            id = (await owner.CreateDocument.Handle(new(project, "Plan 00", "PRIVATE-1"), default)).Value;
            for (var i = 1; i < 23; i++)
            {
                Assert.Equal(OperationStatus.Success, (await owner.UpdateDocument.Handle(new(project, id, i, "Plan 00", "PRIVATE-" + (i + 1)), default)).Status);
                await owner.CreateDocument.Handle(new(project, "Plan " + i.ToString("D2"), "PRIVATE-LIST"), default);
            }
        }
        await using var context = database.OpenContext();
        var repository = new CollaborationReadRepository(context, new TestUser("member"));
        var outsider = new CollaborationReadRepository(context, new TestUser("other"));

        // Act
        var first = await repository.ListDocumentsAsync(project, 0, default);
        var last = await repository.ListDocumentsAsync(project, int.MaxValue, default);
        var revisions = await repository.ListRevisionsAsync(project, id, 1, default);
        var oldest = await repository.ListRevisionsAsync(project, id, int.MaxValue, default);
        var detail = await repository.GetRevisionAsync(project, id, oldest!.Items.Last().Id, default);
        var current = await repository.GetDocumentAsync(project, id, default);

        // Assert
        Assert.Equal(23, first!.Total);
        Assert.Equal(20, first.Items.Count);
        Assert.Equal("Plan 00", first.Items[0].Title);
        Assert.Equal(2, last!.Page);
        Assert.Equal(3, last.Items.Count);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(first));
        Assert.Equal(23, revisions!.Total);
        Assert.Equal(20, revisions.Items.Count);
        Assert.Equal(Enumerable.Range(4, 20).Reverse().Select(i => (long)i), revisions.Items.Select(revision => revision.Version));
        Assert.Equal(new long[] { 3, 2, 1 }, oldest.Items.Select(revision => revision.Version));
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(revisions));
        Assert.Equal("PRIVATE-1", detail!.Body);
        Assert.Equal("PRIVATE-23", current!.Body);
        Assert.Equal(23, current.History.Count);
        Assert.Null(await outsider.GetDocumentAsync(project, id, default));
        Assert.Null(await outsider.ListRevisionsAsync(project, id, 1, default));
        Assert.Null(await repository.GetRevisionAsync(project, int.MaxValue, detail.Revision.Id, default));
        Assert.Equal(DateTimeKind.Utc, detail.Revision.SavedAtUtc.Kind);
        await using (var owner = database.Open()) await owner.RemoveMember.Handle(new(project, 2, "member"), default);
        Assert.Null(await repository.GetDocumentAsync(project, id, default));
        Assert.Null(await repository.ListDocumentsAsync(project, 1, default));
        Assert.Null(await repository.ListRevisionsAsync(project, id, 1, default));
        Assert.Null(await repository.GetRevisionAsync(project, id, detail.Revision.Id, default));
    }

    private static async Task<int> PrepareAsync(ProjectTestDatabase database)
    {
        await database.InitializeAsync();
        await using var owner = database.Open();
        var project = (await owner.Create.Handle(new("AB", "Shared", ""), default)).Value;
        await owner.AddMember.Handle(new(project, 1, "member@example.test"), default);
        return project;
    }

    private static async Task<int> CreateAsync(ProjectTestDatabase database, int project, string title)
    {
        await using var owner = new DocumentSession(database.OpenContext(), new TestUser("owner"));
        return (await owner.CreateDocument.Handle(new(project, title, null), default)).Value;
    }
}

internal class DocumentSession(ApplicationDbContext context, TestUser user) : ProjectSession(context, user)
{
    private CollaborationRepository Documents => new(Context);
    public CreateDocumentHandler CreateDocument => new(Documents, Repository, Access, Unit, User, TimeProvider.System);
    public UpdateDocumentHandler UpdateDocument => new(Documents, Repository, Access, Unit, User, TimeProvider.System);
}
