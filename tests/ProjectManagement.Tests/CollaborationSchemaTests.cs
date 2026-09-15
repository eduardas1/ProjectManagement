using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Domain.Enums;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Tests;

public class CollaborationSchemaTests
{
    [Fact]
    public async Task WorkDatabaseUpgradeRetainsRecordsAndSupportsDocumentEvents()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260129172948_AddWorkItems");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO AspNetUsers (Id,UserName,Email,EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount)
                VALUES ('original','original@example.test','original@example.test',0,0,0,0,0);
            INSERT INTO Projects (Id,Key,Name,Description,OwnerId,NextWorkItemNumber,IsArchived,Version,CreatedAtUtc,UpdatedAtUtc)
                VALUES (1,'OLD','Original','Retained project','original',2,0,9,'2026-10-21 12:00:00','2026-10-21 12:00:00');
            INSERT INTO ProjectMembers (ProjectId,UserId,JoinedAtUtc) VALUES (1,'original','2026-10-21 12:00:00');
            INSERT INTO WorkItems (Id,ProjectId,Number,Type,Title,Description,Status,Priority,CreatedById,IsArchived,Version,CreatedAtUtc,UpdatedAtUtc)
                VALUES (1,1,1,'Task','Retained work','Original body','InProgress','High','original',0,7,'2026-10-21 12:00:00','2026-10-21 12:30:00');
            INSERT INTO ChangeEvents (Id,ProjectId,WorkItemId,ActorId,OccurredAtUtc,Action,Summary,DetailsJson)
                VALUES (1,1,1,'original','2026-10-21 12:30:00','WorkUpdated','Original event','[]');
            """);

        // Act
        await migrator.MigrateAsync();
        var document = NewDocument(1, "original");
        context.ProjectDocuments.Add(document);
        await context.SaveChangesAsync();
        context.DocumentRevisions.Add(NewRevision(1, document.Id, "original"));
        context.ChangeEvents.Add(new ChangeEvent { ProjectId = 1, DocumentId = document.Id, ActorId = "original",
            OccurredAtUtc = DateTime.UtcNow, Action = "DocumentCreated", Summary = "Document created." });
        await context.SaveChangesAsync();

        // Assert
        var project = await context.Projects.SingleAsync();
        var work = await context.WorkItems.SingleAsync();
        var oldEvent = await context.ChangeEvents.SingleAsync(change => change.Id == 1);
        Assert.Equal("Retained project", project.Description);
        Assert.Equal(9, project.Version);
        Assert.Equal(2, project.NextWorkItemNumber);
        Assert.Equal("Retained work", work.Title);
        Assert.Equal("Original body", work.Description);
        Assert.Equal(7, work.Version);
        Assert.Equal(WorkItemStatus.InProgress, work.Status);
        Assert.Equal("Original event", oldEvent.Summary);
        Assert.Equal("[]", oldEvent.DetailsJson);
        Assert.Null(oldEvent.DocumentId);
        Assert.Equal(1, oldEvent.WorkItemId);
        Assert.Single(await context.ProjectMembers.ToListAsync());
        Assert.Single(await context.DocumentRevisions.ToListAsync());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(), name => name.EndsWith("_AddCollaboration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AttachmentsRequireOneSameProjectParentAndMatchingBoundedPayload()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var ids = await PrepareAsync(database);
        await using (var valid = database.OpenContext())
        {
            valid.Attachments.Add(NewAttachment(ids.Project, ids.Work));
            valid.Attachments.Add(NewAttachment(ids.Project, null, ids.Document));
            await valid.SaveChangesAsync();
        }
        var failures = new List<Exception?>();
        var invalid = new List<Attachment>
        {
            NewAttachment(ids.Project, ids.ForeignWork),
            NewAttachment(ids.Project, null),
            NewAttachment(ids.Project, ids.Work, ids.Document),
            NewAttachment(ids.Project, null, ids.ForeignDocument)
        };
        var mismatch = NewAttachment(ids.Project, ids.Work); mismatch.Length = 2; invalid.Add(mismatch);
        var empty = NewAttachment(ids.Project, ids.Work); empty.Length = 0; empty.Contents = []; invalid.Add(empty);
        var oversized = NewAttachment(ids.Project, ids.Work); oversized.Length = 5242881; oversized.Contents = new byte[5242881]; invalid.Add(oversized);

        // Act
        foreach (var attachment in invalid)
        {
            await using var context = database.OpenContext();
            context.Attachments.Add(attachment);
            failures.Add(await Record.ExceptionAsync(() => context.SaveChangesAsync()));
        }
        await using var read = database.OpenContext();
        var saved = await read.Attachments.OrderBy(attachment => attachment.Id).ToListAsync();

        // Assert
        Assert.All(failures, failure => Assert.IsType<DbUpdateException>(failure));
        Assert.Equal(2, saved.Count);
        Assert.Equal(ids.Work, saved[0].WorkItemId);
        Assert.Equal(ids.Document, saved[1].DocumentId);
        Assert.All(saved, attachment => Assert.Equal(new byte[] { 65, 66, 67 }, attachment.Contents));
        Assert.All(saved, attachment => Assert.Equal(DateTimeKind.Utc, attachment.UploadedAtUtc.Kind));
    }

    [Fact]
    public async Task RemovedCommentRequiresClearedTextAndAnotherAuthorsRemovalReason()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var ids = await PrepareAsync(database);
        int id;
        await using (var context = database.OpenContext())
        {
            var comment = new Comment { ProjectId = ids.Project, WorkItemId = ids.Work, AuthorId = "member",
                Body = "Private comment body", CreatedAtUtc = DateTime.UtcNow };
            context.Comments.Add(comment); await context.SaveChangesAsync(); id = comment.Id;
        }

        // Act
        await using var retainedText = database.OpenContext();
        var first = await retainedText.Comments.SingleAsync();
        first.IsDeleted = true; first.DeletedById = "owner"; first.DeletedAtUtc = DateTime.UtcNow; first.ModerationReason = "Off topic";
        var retainedFailure = await Record.ExceptionAsync(() => retainedText.SaveChangesAsync());
        await using var missingReason = database.OpenContext();
        var second = await missingReason.Comments.SingleAsync();
        second.IsDeleted = true; second.Body = ""; second.DeletedById = "owner"; second.DeletedAtUtc = DateTime.UtcNow;
        var reasonFailure = await Record.ExceptionAsync(() => missingReason.SaveChangesAsync());
        await using (var valid = database.OpenContext())
        {
            var comment = await valid.Comments.FindAsync(id);
            comment!.IsDeleted = true; comment.Body = ""; comment.Version = 2; comment.DeletedById = "owner";
            comment.DeletedAtUtc = DateTime.UtcNow; comment.UpdatedAtUtc = comment.DeletedAtUtc; comment.ModerationReason = "Off topic";
            await valid.SaveChangesAsync();
        }
        await using var read = database.OpenContext();
        var saved = await read.Comments.SingleAsync();

        // Assert
        Assert.IsType<DbUpdateException>(retainedFailure);
        Assert.IsType<DbUpdateException>(reasonFailure);
        Assert.True(saved.IsDeleted);
        Assert.Equal("", saved.Body);
        Assert.Equal("member", saved.AuthorId);
        Assert.Equal("owner", saved.DeletedById);
        Assert.Equal("Off topic", saved.ModerationReason);
        Assert.Equal(2, saved.Version);
        Assert.Equal(DateTimeKind.Utc, saved.DeletedAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, saved.UpdatedAtUtc!.Value.Kind);
    }

    [Fact]
    public async Task RevisionsAndEventsKeepUniqueVersionAndSameProjectDocumentScope()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var ids = await PrepareAsync(database);
        await using (var context = database.OpenContext())
        {
            context.DocumentRevisions.Add(NewRevision(ids.Project, ids.Document));
            context.ChangeEvents.Add(new ChangeEvent { ProjectId = ids.Project, DocumentId = ids.Document,
                ActorId = "owner", OccurredAtUtc = DateTime.UtcNow, Action = "DocumentCreated", Summary = "Document created." });
            await context.SaveChangesAsync();
        }

        // Act
        await using var duplicate = database.OpenContext();
        duplicate.DocumentRevisions.Add(NewRevision(ids.Project, ids.Document));
        var duplicateFailure = await Record.ExceptionAsync(() => duplicate.SaveChangesAsync());
        await using var foreign = database.OpenContext();
        foreign.DocumentRevisions.Add(NewRevision(ids.Project, ids.ForeignDocument));
        var foreignFailure = await Record.ExceptionAsync(() => foreign.SaveChangesAsync());
        await using var mixedEvent = database.OpenContext();
        mixedEvent.ChangeEvents.Add(new ChangeEvent { ProjectId = ids.Project, WorkItemId = ids.Work, DocumentId = ids.Document,
            ActorId = "owner", OccurredAtUtc = DateTime.UtcNow, Action = "Bad", Summary = "Bad target" });
        var mixedFailure = await Record.ExceptionAsync(() => mixedEvent.SaveChangesAsync());
        await using var foreignEvent = database.OpenContext();
        foreignEvent.ChangeEvents.Add(new ChangeEvent { ProjectId = ids.Project, DocumentId = ids.ForeignDocument,
            ActorId = "owner", OccurredAtUtc = DateTime.UtcNow, Action = "Bad", Summary = "Bad target" });
        var eventFailure = await Record.ExceptionAsync(() => foreignEvent.SaveChangesAsync());
        await using var read = database.OpenContext();

        // Assert
        Assert.All(new[] { duplicateFailure, foreignFailure, mixedFailure, eventFailure }, failure => Assert.IsType<DbUpdateException>(failure));
        Assert.Single(await read.DocumentRevisions.ToListAsync());
        Assert.Equal(ids.Document, (await read.ChangeEvents.Where(change => change.DocumentId != null).SingleAsync()).DocumentId);
    }

    private static ProjectDocument NewDocument(int projectId, string userId = "owner") => new()
    {
        ProjectId = projectId, Title = "Notes", Body = "", CreatedById = userId, UpdatedById = userId,
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };

    private static DocumentRevision NewRevision(int projectId, int documentId, string userId = "owner") => new()
    {
        ProjectId = projectId, DocumentId = documentId, Version = 1, Title = "Notes", Body = "",
        EditorId = userId, SavedAtUtc = DateTime.UtcNow
    };

    private static Attachment NewAttachment(int projectId, int? workItemId, int? documentId = null) => new()
    {
        ProjectId = projectId, WorkItemId = workItemId, DocumentId = documentId, StoredName = Guid.NewGuid().ToString("N"),
        DisplayName = "data.txt", ContentType = "text/plain", Length = 3, Contents = [65, 66, 67],
        UploadedById = "owner", UploadedAtUtc = DateTime.UtcNow
    };

    private static async Task<(int Project, int ForeignProject, int Work, int ForeignWork, int Document, int ForeignDocument)> PrepareAsync(ProjectTestDatabase database)
    {
        await database.InitializeAsync();
        int project, foreign;
        await using (var session = database.Open())
        {
            project = (await session.Create.Handle(new("AB", "First", ""), default)).Value;
            foreign = (await session.Create.Handle(new("CD", "Second", ""), default)).Value;
        }
        await using var context = database.OpenContext();
        var work = new WorkItem { ProjectId = project, Number = 1, Type = WorkItemType.Task, Title = "First",
            CreatedById = "owner", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var other = new WorkItem { ProjectId = foreign, Number = 1, Type = WorkItemType.Task, Title = "Second",
            CreatedById = "owner", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var document = NewDocument(project); var otherDocument = NewDocument(foreign);
        context.WorkItems.AddRange(work, other); context.ProjectDocuments.AddRange(document, otherDocument);
        await context.SaveChangesAsync();
        return (project, foreign, work.Id, other.Id, document.Id, otherDocument.Id);
    }
}
