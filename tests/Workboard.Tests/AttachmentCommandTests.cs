using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workboard.Application.Attachments;
using Workboard.Application.Common;
using Workboard.Domain.Enums;
using Workboard.Infrastructure.Data;
using Workboard.Infrastructure.Repositories;

namespace Workboard.Tests;

public class AttachmentCommandTests
{
    [Fact]
    public async Task BothTargetsKeepExactBytesServerMetadataAndOwnOrOwnerRemoval()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var (project, work, document) = await PrepareAsync(database);
        var bytes = Enumerable.Repeat((byte)'a', AttachmentFile.MaxBytes).ToArray();
        int workFile, documentFile;
        await using var member = new AttachmentSession(database.OpenContext(), new TestUser("member"));
        await using var owner = new AttachmentSession(database.OpenContext(), new TestUser("owner"));

        // Act
        var uploaded = await member.Upload.Handle(new(project, work, null, "Šaltinis.TXT", bytes), default);
        Assert.Equal(OperationStatus.Success, uploaded.Status); workFile = uploaded.Value;
        var image = await owner.Upload.Handle(new(project, null, document, "Image.png", AttachmentFileTests.Png), default);
        Assert.Equal(OperationStatus.Success, image.Status); documentFile = image.Value;
        await using (var read = database.OpenContext())
        {
            var rows = await read.Attachments.OrderBy(attachment => attachment.Id).ToListAsync();
            Assert.Equal(bytes, rows[0].Contents);
            Assert.Equal("text/plain", rows[0].ContentType);
            Assert.Equal(AttachmentFile.MaxBytes, rows[0].Length);
            Assert.Equal("member", rows[0].UploadedById);
            Assert.Equal(AttachmentFileTests.Png, rows[1].Contents);
            Assert.Equal("image/png", rows[1].ContentType);
            Assert.NotEqual(rows[0].StoredName, rows[1].StoredName);
            Assert.All(rows, row => { Assert.Matches("^[a-f0-9]{32}$", row.StoredName); Assert.Equal(1, row.Version); Assert.Equal(DateTimeKind.Utc, row.UploadedAtUtc.Kind); });
        }
        Assert.Equal(OperationStatus.NotFound, (await member.RemoveFile.Handle(new(project, documentFile, 1), default)).Status);
        Assert.Equal(OperationStatus.Conflict, (await owner.RemoveFile.Handle(new(project, workFile, 2), default)).Status);
        Assert.Equal(OperationStatus.Success, (await owner.RemoveFile.Handle(new(project, workFile, 1), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await owner.RemoveFile.Handle(new(project, workFile, 1), default)).Status);
        Assert.Equal(OperationStatus.Success, (await owner.RemoveFile.Handle(new(project, documentFile, 1), default)).Status);
        var own = (await member.Upload.Handle(new(project, null, document, "Own.txt", Encoding.UTF8.GetBytes("Own content")), default)).Value;
        Assert.Equal(OperationStatus.Success, (await member.RemoveFile.Handle(new(project, own, 1), default)).Status);
        await using var context = database.OpenContext();
        var events = await context.ChangeEvents.Where(change => change.Action.StartsWith("Attachment")).ToListAsync();

        // Assert
        Assert.Empty(await context.Attachments.ToListAsync());
        Assert.Equal(6, events.Count);
        Assert.All(events, change =>
        {
            Assert.True(change.WorkItemId.HasValue != change.DocumentId.HasValue);
            using var payload = JsonDocument.Parse(change.DetailsJson);
            Assert.Single(payload.RootElement.EnumerateObject());
            Assert.True(payload.RootElement.GetProperty("AttachmentId").GetInt32() > 0);
            Assert.DoesNotContain("Šaltinis", change.Summary + change.DetailsJson);
        });
        Assert.Equal(1, (await context.WorkItems.SingleAsync()).Version);
        Assert.Equal(1, (await context.ProjectDocuments.SingleAsync()).Version);
        Assert.Equal(3, (await context.Projects.SingleAsync()).Version);
    }

    [Fact]
    public async Task CurrentRolesArchiveAndRevocationControlWritesAndEveryRead()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var (project, work, document) = await PrepareAsync(database);
        var bytes = Encoding.UTF8.GetBytes("PRIVATE-BLOB");
        var workFile = await UploadAsync(database, project, work, null, bytes);
        var documentFile = await UploadAsync(database, project, null, document, bytes);
        await using (var owner = database.Open()) await owner.AddMember.Handle(new(project, 3, "other@example.test"), default);
        await using (var other = new AttachmentSession(database.OpenContext(), new TestUser("other")))
            Assert.Equal(OperationStatus.NotFound, (await other.RemoveFile.Handle(new(project, workFile, 1), default)).Status);
        await using (var owner = database.Open()) await owner.Transfer.Handle(new(project, 4, "other"), default);
        await using var readContext = database.OpenContext();
        var repository = new CollaborationReadRepository(readContext, new TestUser("member"));

        // Act
        await using (var former = new AttachmentSession(database.OpenContext(), new TestUser("owner")))
            Assert.Equal(OperationStatus.NotFound, (await former.RemoveFile.Handle(new(project, workFile, 1), default)).Status);
        await using (var current = new DocumentSession(database.OpenContext(), new TestUser("other")))
            await current.ArchiveDocument.Handle(new(project, document, 1, true), default);
        await using (var current = new WorkItemSession(database.OpenContext(), new TestUser("other")))
            await current.ArchiveWork.Handle(new(project, work, 1, true), default);
        await using (var member = new AttachmentSession(database.OpenContext(), new TestUser("member")))
        {
            Assert.Equal(OperationStatus.Invalid, (await member.Upload.Handle(new(project, work, null, "Note.txt", bytes), default)).Status);
            Assert.Equal(OperationStatus.Invalid, (await member.Upload.Handle(new(project, null, document, "Note.txt", bytes), default)).Status);
            Assert.Equal(OperationStatus.Invalid, (await member.RemoveFile.Handle(new(project, workFile, 1), default)).Status);
            Assert.Equal(OperationStatus.Invalid, (await member.RemoveFile.Handle(new(project, documentFile, 1), default)).Status);
        }
        Assert.Equal(bytes, (await repository.DownloadAttachmentAsync(project, workFile, default))!.Contents);
        Assert.Equal(bytes, (await repository.DownloadAttachmentAsync(project, documentFile, default))!.Contents);
        Assert.True((await repository.ListAttachmentsAsync(project, null, document, 1, default))!.Target.IsArchived);
        await using (var current = new ProjectSession(database.OpenContext(), new TestUser("other")))
        {
            await current.Archive.Handle(new(project, 5, true), default);
            await current.RemoveMember.Handle(new(project, 6, "member"), default);
        }
        await using var revoked = new AttachmentSession(database.OpenContext(), new TestUser("member"));

        // Assert
        Assert.Equal(OperationStatus.NotFound, (await revoked.Upload.Handle(new(project, work, null, "Note.txt", bytes), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await revoked.RemoveFile.Handle(new(project, workFile, 1), default)).Status);
        Assert.Null(await repository.GetAttachmentAsync(project, workFile, default));
        Assert.Null(await repository.DownloadAttachmentAsync(project, workFile, default));
        Assert.Null(await repository.DownloadAttachmentAsync(project, documentFile, default));
        Assert.Null(await repository.ListAttachmentsAsync(project, work, null, 1, default));
        Assert.Null(await repository.GetAttachmentTargetAsync(project, null, document, default));
        await using var stored = database.OpenContext();
        Assert.Equal(2, await stored.Attachments.CountAsync());
        Assert.Equal(2, await stored.ChangeEvents.CountAsync(change => change.Action.StartsWith("Attachment")));
    }

    [Fact]
    public async Task InvalidTargetsAndContentNeverPublishMetadataOrEvents()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var (project, work, document) = await PrepareAsync(database);
        int foreign;
        await using (var owner = database.Open()) foreign = (await owner.Create.Handle(new("CD", "Private", ""), default)).Value;
        int foreignWork;
        await using (var owner = new WorkItemSession(database.OpenContext(), new TestUser("owner")))
            foreignWork = (await owner.CreateWork.Handle(new(foreign, WorkItemType.Task, "Other", "", WorkItemPriority.Medium, null, null, null, null, []), default)).Value;
        int foreignDocument;
        await using (var owner = new DocumentSession(database.OpenContext(), new TestUser("owner")))
            foreignDocument = (await owner.CreateDocument.Handle(new(foreign, "Private", null), default)).Value;
        var bytes = Encoding.UTF8.GetBytes("Content");
        await using var member = new AttachmentSession(database.OpenContext(), new TestUser("member"));

        // Act
        Assert.Equal(OperationStatus.Invalid, (await member.Upload.Handle(new(project, null, null, "Note.txt", bytes), default)).Status);
        Assert.Equal(OperationStatus.Invalid, (await member.Upload.Handle(new(project, work, document, "Note.txt", bytes), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await member.Upload.Handle(new(project, foreignWork, null, "Note.txt", bytes), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await member.Upload.Handle(new(project, null, foreignDocument, "Note.txt", bytes), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await member.Upload.Handle(new(foreign, foreignWork, null, "Note.txt", bytes), default)).Status);
        Assert.Equal(OperationStatus.Invalid, (await member.Upload.Handle(new(project, work, null, "../Note.txt", bytes), default)).Status);
        Assert.Equal(OperationStatus.Invalid, (await member.Upload.Handle(new(project, work, null, "Fake.pdf", bytes), default)).Status);
        Assert.Equal(OperationStatus.Invalid, (await member.Upload.Handle(new(project, work, null, "Huge.txt", new byte[AttachmentFile.MaxBytes + 1]), default)).Status);
        await using var context = database.OpenContext();

        // Assert
        Assert.Empty(await context.Attachments.ToListAsync());
        Assert.Empty(await context.ChangeEvents.Where(change => change.Action.StartsWith("Attachment")).ToListAsync());
        Assert.Equal(1, (await context.WorkItems.SingleAsync(item => item.Id == work)).Version);
    }

    [Fact]
    public async Task FailedEventRollsBackBlobAllocationAndPhysicalRemoval()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var (project, work, document) = await PrepareAsync(database);
        var bytes = AttachmentFileTests.Png;

        // Act
        await using (var failing = new AttachmentSession(database.OpenContext(new RejectProjectEvent()), new TestUser("member")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Upload.Handle(new(project, null, document, "Image.png", bytes), default));
        await using (var context = database.OpenContext()) Assert.Empty(await context.Attachments.ToListAsync());
        var id = await UploadAsync(database, project, work, null, bytes, "Image.png");
        await using (var failing = new AttachmentSession(database.OpenContext(new RejectCompletedAttachmentSave()), new TestUser("owner")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.RemoveFile.Handle(new(project, id, 1), default));
        await using var read = database.OpenContext();
        var attachment = await read.Attachments.SingleAsync();

        // Assert
        Assert.Equal(id, attachment.Id);
        Assert.Equal(bytes, attachment.Contents);
        Assert.Equal(1, attachment.Version);
        Assert.Single(await read.ChangeEvents.Where(change => change.Action.StartsWith("Attachment")).ToListAsync());
    }

    [Fact]
    public async Task ScopedMetadataPagesDoNotExposeStoredNamesOrContents()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var (project, work, document) = await PrepareAsync(database);
        var ids = new List<int>();
        await using (var member = new AttachmentSession(database.OpenContext(), new TestUser("member"), new AttachmentTestClock()))
            for (var i = 0; i < 23; i++) ids.Add((await member.Upload.Handle(new(project, work, null, "Note " + i + ".txt", Encoding.UTF8.GetBytes("PRIVATE-BLOB-" + i)), default)).Value);
        await using var context = database.OpenContext();
        var memberRead = new CollaborationReadRepository(context, new TestUser("member"));
        var ownerRead = new CollaborationReadRepository(context, new TestUser("owner"));
        var outsiderRead = new CollaborationReadRepository(context, new TestUser("other"));

        // Act
        var first = await memberRead.ListAttachmentsAsync(project, work, null, 0, default);
        var last = await memberRead.ListAttachmentsAsync(project, work, null, int.MaxValue, default);
        var download = await memberRead.DownloadAttachmentAsync(project, ids[0], default);
        var owner = await ownerRead.GetAttachmentAsync(project, ids[0], default);
        var serial = JsonSerializer.Serialize(first);

        // Assert
        Assert.Equal(23, first!.Total);
        Assert.Equal(ids.AsEnumerable().Reverse().Take(20), first.Items.Select(item => item.Id));
        Assert.Equal(2, last!.Page);
        Assert.Equal(ids.AsEnumerable().Reverse().Skip(20), last.Items.Select(item => item.Id));
        Assert.DoesNotContain("Contents", serial);
        Assert.DoesNotContain("StoredName", serial);
        Assert.DoesNotContain("PRIVATE", serial);
        Assert.Equal(Encoding.UTF8.GetBytes("PRIVATE-BLOB-0"), download!.Contents);
        Assert.True(owner!.Attachment.CanRemove);
        Assert.Null(await outsiderRead.DownloadAttachmentAsync(project, ids[0], default));
        Assert.Null(await outsiderRead.GetAttachmentAsync(project, ids[0], default));
        Assert.Null(await outsiderRead.ListAttachmentsAsync(project, work, null, 1, default));
        Assert.Null(await memberRead.GetAttachmentTargetAsync(project, work, document, default));
        Assert.Empty((await memberRead.ListAttachmentsAsync(project, null, document, 1, default))!.Items);
    }

    private static async Task<(int Project, int Work, int Document)> PrepareAsync(ProjectTestDatabase database)
    {
        await database.InitializeAsync();
        int project, work, document;
        await using (var owner = database.Open())
        {
            project = (await owner.Create.Handle(new("AB", "Shared", ""), default)).Value;
            await owner.AddMember.Handle(new(project, 1, "member@example.test"), default);
        }
        await using (var owner = new WorkItemSession(database.OpenContext(), new TestUser("owner")))
            work = (await owner.CreateWork.Handle(new(project, WorkItemType.Task, "Task", "", WorkItemPriority.Medium, null, null, null, null, []), default)).Value;
        await using (var owner = new DocumentSession(database.OpenContext(), new TestUser("owner")))
            document = (await owner.CreateDocument.Handle(new(project, "Plan", null), default)).Value;
        return (project, work, document);
    }

    private static async Task<int> UploadAsync(ProjectTestDatabase database, int project, int? work, int? document, byte[] contents, string name = "Note.txt")
    {
        await using var member = new AttachmentSession(database.OpenContext(), new TestUser("member"));
        var result = await member.Upload.Handle(new(project, work, document, name, contents), default);
        Assert.Equal(OperationStatus.Success, result.Status);
        return result.Value;
    }
}

internal class AttachmentSession(ApplicationDbContext context, TestUser user, TimeProvider? clock = null) : ProjectSession(context, user)
{
    private CollaborationRepository Files => new(Context);
    private WorkItemRepository Work => new(Context);
    private TimeProvider Clock => clock ?? TimeProvider.System;
    public UploadAttachmentHandler Upload => new(Files, Work, Repository, Access, Unit, User, Clock);
    public RemoveAttachmentHandler RemoveFile => new(Files, Work, Repository, Access, Unit, User, Clock);
}

internal class AttachmentTestClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
}

internal class RejectCompletedAttachmentSave : SaveChangesInterceptor
{
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Failure after attachment SQL was saved, before commit.");
}
