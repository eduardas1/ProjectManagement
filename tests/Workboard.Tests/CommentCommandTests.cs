using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workboard.Application.Comments;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;
using Workboard.Domain.Enums;
using Workboard.Infrastructure.Data;
using Workboard.Infrastructure.Repositories;

namespace Workboard.Tests;

public class CommentCommandTests
{
    [Fact]
    public async Task OwnCommentLifecyclePreservesPrivacyAndParentVersions()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var (project, work) = await PrepareAsync(database);
        const string original = "ORIGINAL-PRIVATE-COMMENT";
        const string edited = "EDITED-PRIVATE-COMMENT";
        await using var member = new CommentSession(database.OpenContext(), new TestUser("member"));

        // Act
        var created = await member.CreateComment.Handle(new(project, work, "  " + original + "  "), default);
        Assert.Equal(OperationStatus.Success, created.Status);
        var id = created.Value;
        Assert.Null((await member.Context.Comments.SingleAsync()).UpdatedAtUtc);
        Assert.Equal(OperationStatus.Success, (await member.UpdateComment.Handle(new(project, work, id, 1, edited), default)).Status);
        Assert.Equal(OperationStatus.Conflict, (await member.UpdateComment.Handle(new(project, work, id, 1, edited), default)).Status);
        Assert.Equal(OperationStatus.Success, (await member.UpdateComment.Handle(new(project, work, id, 2, " " + edited + " "), default)).Status);
        Assert.Equal(OperationStatus.Success, (await member.RemoveComment.Handle(new(project, work, id, 2, null), default)).Status);
        Assert.Equal(OperationStatus.Conflict, (await member.RemoveComment.Handle(new(project, work, id, 2, null), default)).Status);
        Assert.Equal(OperationStatus.Success, (await member.RemoveComment.Handle(new(project, work, id, 3, "Late reason"), default)).Status);
        Assert.Equal(OperationStatus.Invalid, (await member.UpdateComment.Handle(new(project, work, id, 3, "Restored"), default)).Status);
        await using var read = database.OpenContext();
        var comment = await read.Comments.SingleAsync();
        var events = await read.ChangeEvents.Where(change => change.Action.StartsWith("Comment")).OrderBy(change => change.Id).ToListAsync();

        // Assert
        Assert.True(comment.IsDeleted);
        Assert.Equal("", comment.Body);
        Assert.Equal(3, comment.Version);
        Assert.Equal("member", comment.AuthorId);
        Assert.Equal("member", comment.DeletedById);
        Assert.Null(comment.ModerationReason);
        Assert.Equal(DateTimeKind.Utc, comment.DeletedAtUtc!.Value.Kind);
        Assert.Equal(new[] { "CommentCreated", "CommentUpdated", "CommentRemoved" }, events.Select(change => change.Action));
        Assert.All(events, change =>
        {
            Assert.Equal("member", change.ActorId);
            Assert.Equal(work, change.WorkItemId);
            Assert.Null(change.DocumentId);
            Assert.DoesNotContain(original, change.Summary + change.DetailsJson);
            Assert.DoesNotContain(edited, change.Summary + change.DetailsJson);
            using var details = JsonDocument.Parse(change.DetailsJson);
            Assert.Equal(id, details.RootElement.GetProperty("CommentId").GetInt32());
            Assert.Single(details.RootElement.EnumerateObject());
        });
        Assert.Equal(1, (await read.WorkItems.SingleAsync()).Version);
        Assert.Equal(WorkItemStatus.Backlog, (await read.WorkItems.SingleAsync()).Status);
        var parent = await read.Projects.SingleAsync();
        Assert.Equal(3, parent.Version);
        Assert.Equal(2, parent.NextWorkItemNumber);
    }

    [Fact]
    public async Task CurrentOwnerModeratesButCannotEditAnotherAuthorsComment()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var (project, work) = await PrepareAsync(database);
        var id = await CreateAsync(database, project, work, "Private body");
        await using var owner = new CommentSession(database.OpenContext(), new TestUser("owner"));
        await using var outsider = new CommentSession(database.OpenContext(), new TestUser("other"));

        // Act
        var forbiddenEdit = await owner.UpdateComment.Handle(new(project, work, id, 1, "Owner edit"), default);
        var outsiderRemove = await outsider.RemoveComment.Handle(new(project, work, id, 1, "No access"), default);
        var missingReason = await owner.RemoveComment.Handle(new(project, work, id, 1, "  "), default);
        var longReason = await owner.RemoveComment.Handle(new(project, work, id, 1, new string('x', 501)), default);
        await using (var admin = database.Open()) await admin.AddMember.Handle(new(project, 3, "other@example.test"), default);
        await using (var admin = database.Open()) await admin.Transfer.Handle(new(project, 4, "other"), default);
        await using var formerOwner = new CommentSession(database.OpenContext(), new TestUser("owner"));
        var formerRemove = await formerOwner.RemoveComment.Handle(new(project, work, id, 1, "Former role"), default);
        await using var newOwner = new CommentSession(database.OpenContext(), new TestUser("other"));
        var removed = await newOwner.RemoveComment.Handle(new(project, work, id, 1, " Off topic "), default);
        var repeated = await newOwner.RemoveComment.Handle(new(project, work, id, 2, "New reason"), default);
        await using var read = database.OpenContext();
        var comment = await read.Comments.SingleAsync();

        // Assert
        Assert.Equal(OperationStatus.NotFound, forbiddenEdit.Status);
        Assert.Equal(OperationStatus.NotFound, outsiderRemove.Status);
        Assert.Equal(OperationStatus.Invalid, missingReason.Status);
        Assert.Equal("ModerationReason", missingReason.Field);
        Assert.Equal(OperationStatus.Invalid, longReason.Status);
        Assert.Equal(OperationStatus.NotFound, formerRemove.Status);
        Assert.Equal(OperationStatus.Success, removed.Status);
        Assert.Equal(OperationStatus.Success, repeated.Status);
        Assert.Equal("", comment.Body);
        Assert.Equal("member", comment.AuthorId);
        Assert.Equal("other", comment.DeletedById);
        Assert.Equal("Off topic", comment.ModerationReason);
        Assert.Equal(2, comment.Version);
        Assert.Single(await read.ChangeEvents.Where(change => change.Action == "CommentRemoved").ToListAsync());
    }

    [Fact]
    public async Task InvalidBodyForeignParentArchiveAndRevocationLeaveCommentUntouched()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var (project, work) = await PrepareAsync(database);
        var id = await CreateAsync(database, project, work, "Retained body");
        int foreignProject, foreignWork;
        await using (var owner = database.Open()) foreignProject = (await owner.Create.Handle(new("CD", "Private", ""), default)).Value;
        await using (var owner = new WorkItemSession(database.OpenContext(), new TestUser("owner")))
            foreignWork = (await owner.CreateWork.Handle(new(foreignProject, WorkItemType.Task, "Other", "", WorkItemPriority.Medium, null, null, null, null, []), default)).Value;
        await using var member = new CommentSession(database.OpenContext(), new TestUser("member"));
        await using var readContext = database.OpenContext();
        var repository = new CollaborationReadRepository(readContext, new TestUser("member"));

        // Act
        Assert.Equal(OperationStatus.Invalid, (await member.CreateComment.Handle(new(project, work, " "), default)).Status);
        Assert.Equal(OperationStatus.Invalid, (await member.CreateComment.Handle(new(project, work, new string('x', 5001)), default)).Status);
        Assert.Equal(OperationStatus.Invalid, (await member.UpdateComment.Handle(new(project, work, id, 1, " "), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await member.CreateComment.Handle(new(foreignProject, foreignWork, "Secret"), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await member.CreateComment.Handle(new(project, foreignWork, "Wrong parent"), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await member.UpdateComment.Handle(new(project, foreignWork, id, 1, "Wrong parent"), default)).Status);
        await using (var owner = new WorkItemSession(database.OpenContext(), new TestUser("owner"))) await owner.ArchiveWork.Handle(new(project, work, 1, true), default);
        Assert.True((await repository.ListCommentsAsync(project, work, 1, default))!.Work.IsArchived);
        await using (var archived = new CommentSession(database.OpenContext(), new TestUser("member")))
        {
            Assert.Equal(OperationStatus.Invalid, (await archived.CreateComment.Handle(new(project, work, "Archived"), default)).Status);
            Assert.Equal(OperationStatus.Invalid, (await archived.UpdateComment.Handle(new(project, work, id, 1, "Archived"), default)).Status);
            Assert.Equal(OperationStatus.Invalid, (await archived.RemoveComment.Handle(new(project, work, id, 1, null), default)).Status);
        }
        await using (var owner = new WorkItemSession(database.OpenContext(), new TestUser("owner"))) await owner.ArchiveWork.Handle(new(project, work, 2, false), default);
        await using (var owner = database.Open()) await owner.Archive.Handle(new(project, 3, true), default);
        Assert.True((await repository.ListCommentsAsync(project, work, 1, default))!.Work.Project.IsArchived);
        await using (var current = new CommentSession(database.OpenContext(), new TestUser("member")))
            Assert.Equal(OperationStatus.Invalid, (await current.UpdateComment.Handle(new(project, work, id, 1, "Archived project"), default)).Status);
        await using (var owner = database.Open()) await owner.RemoveMember.Handle(new(project, 4, "member"), default);
        await using (var revoked = new CommentSession(database.OpenContext(), new TestUser("member")))
        {
            Assert.Equal(OperationStatus.NotFound, (await revoked.UpdateComment.Handle(new(project, work, id, 1, "Revoked"), default)).Status);
            Assert.Equal(OperationStatus.NotFound, (await revoked.RemoveComment.Handle(new(project, work, id, 1, null), default)).Status);
            Assert.Equal(OperationStatus.NotFound, (await revoked.CreateComment.Handle(new(project, work, "Revoked"), default)).Status);
        }

        // Assert
        Assert.Null(await repository.ListCommentsAsync(project, work, 1, default));
        Assert.Null(await repository.GetCommentAsync(project, work, id, default));
        await using var read = database.OpenContext();
        var comment = await read.Comments.SingleAsync();
        Assert.Equal("Retained body", comment.Body);
        Assert.Equal(1, comment.Version);
        Assert.False(comment.IsDeleted);
        Assert.Single(await read.ChangeEvents.Where(change => change.Action.StartsWith("Comment")).ToListAsync());
    }

    [Fact]
    public async Task FailedEventRollsBackAllocatedCommentBodyEditAndRemoval()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var (project, work) = await PrepareAsync(database);

        // Act
        await using (var failing = new CommentSession(database.OpenContext(new RejectProjectEvent()), new TestUser("member")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.CreateComment.Handle(new(project, work, "Not published"), default));
        await using (var read = database.OpenContext()) Assert.Empty(await read.Comments.ToListAsync());
        var id = await CreateAsync(database, project, work, "Original");
        await using (var failing = new CommentSession(database.OpenContext(new RejectProjectEvent()), new TestUser("member")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.UpdateComment.Handle(new(project, work, id, 1, "Not saved"), default));
        await using (var failing = new CommentSession(database.OpenContext(new RejectProjectEvent()), new TestUser("member")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.RemoveComment.Handle(new(project, work, id, 1, null), default));
        await using var context = database.OpenContext();
        var comment = await context.Comments.SingleAsync();

        // Assert
        Assert.Equal("Original", comment.Body);
        Assert.Equal(1, comment.Version);
        Assert.Null(comment.UpdatedAtUtc);
        Assert.False(comment.IsDeleted);
        Assert.Null(comment.DeletedById);
        Assert.Single(await context.ChangeEvents.Where(change => change.Action.StartsWith("Comment")).ToListAsync());
        Assert.Equal(1, (await context.WorkItems.SingleAsync()).Version);
    }

    [Fact]
    public async Task ScopedReadPagesKeepRemovalMarkerAndCurrentRoleWithoutBodyHistory()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var (project, work) = await PrepareAsync(database);
        var clock = new CommentTestClock();
        var ids = new List<int>();
        await using (var member = new CommentSession(database.OpenContext(), new TestUser("member"), clock))
        {
            for (var i = 0; i < 23; i++) ids.Add((await member.CreateComment.Handle(new(project, work, "Body " + i), default)).Value);
            await member.RemoveComment.Handle(new(project, work, ids[0], 1, null), default);
        }
        await using var context = database.OpenContext();
        var memberRead = new CollaborationReadRepository(context, new TestUser("member"));
        var ownerRead = new CollaborationReadRepository(context, new TestUser("owner"));
        var outsiderRead = new CollaborationReadRepository(context, new TestUser("other"));

        // Act
        var first = await memberRead.ListCommentsAsync(project, work, 0, default);
        var last = await memberRead.ListCommentsAsync(project, work, int.MaxValue, default);
        var removed = await memberRead.GetCommentAsync(project, work, ids[0], default);
        var owner = await ownerRead.GetCommentAsync(project, work, ids[1], default);

        // Assert
        Assert.Equal(23, first!.Total);
        Assert.Equal(1, first.Page);
        Assert.Equal(ids.AsEnumerable().Reverse().Take(20), first.Items.Select(item => item.Id));
        Assert.Equal(2, last!.Page);
        Assert.Equal(ids.AsEnumerable().Reverse().Skip(20), last.Items.Select(item => item.Id));
        Assert.Equal("", removed!.Comment.Body);
        Assert.Equal("member@example.test", removed.Comment.Author);
        Assert.Equal("member@example.test", removed.Comment.DeletedBy);
        Assert.True(removed.Comment.IsDeleted);
        Assert.True(removed.Comment.IsAuthor);
        Assert.False(removed.Comment.CanRemove);
        Assert.False(owner!.Comment.IsAuthor);
        Assert.True(owner.Comment.CanRemove);
        Assert.Null(await outsiderRead.ListCommentsAsync(project, work, 1, default));
        Assert.Null(await outsiderRead.GetCommentWorkAsync(project, work, default));
        Assert.Null(await memberRead.GetCommentAsync(project, int.MaxValue, ids[0], default));
        Assert.Null(await memberRead.GetCommentAsync(project, work, int.MaxValue, default));
        Assert.All(first.Items, item => Assert.Equal(DateTimeKind.Utc, item.CreatedAtUtc.Kind));
    }

    private static async Task<(int Project, int Work)> PrepareAsync(ProjectTestDatabase database)
    {
        await database.InitializeAsync();
        int project;
        await using (var owner = database.Open())
        {
            project = (await owner.Create.Handle(new("AB", "Shared", ""), default)).Value;
            await owner.AddMember.Handle(new(project, 1, "member@example.test"), default);
        }
        await using var creator = new WorkItemSession(database.OpenContext(), new TestUser("owner"));
        var work = (await creator.CreateWork.Handle(new(project, WorkItemType.Task, "Task", "", WorkItemPriority.Medium, null, null, null, null, []), default)).Value;
        return (project, work);
    }

    private static async Task<int> CreateAsync(ProjectTestDatabase database, int projectId, int workItemId, string body)
    {
        await using var member = new CommentSession(database.OpenContext(), new TestUser("member"));
        return (await member.CreateComment.Handle(new(projectId, workItemId, body), default)).Value;
    }
}

internal class CommentSession(ApplicationDbContext context, TestUser user, TimeProvider? clock = null) : ProjectSession(context, user)
{
    private ICollaborationRepository Comments => new CollaborationRepository(Context);
    private IWorkItemRepository Work => new WorkItemRepository(Context);
    private TimeProvider Clock => clock ?? TimeProvider.System;
    public CreateCommentHandler CreateComment => new(Comments, Work, Repository, Access, Unit, User, Clock);
    public UpdateCommentHandler UpdateComment => new(Comments, Work, Repository, Access, Unit, User, Clock);
    public RemoveCommentHandler RemoveComment => new(Comments, Work, Repository, Access, Unit, User, Clock);
}

internal class CommentTestClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
}
