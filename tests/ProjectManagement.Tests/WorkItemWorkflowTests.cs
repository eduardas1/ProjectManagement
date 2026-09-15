using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.WorkItems;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Tests;

public class WorkItemWorkflowTests
{
    [Fact]
    public async Task LifecyclePersistsOneVersionAndTypedEventPerActualChange()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var id = await CreateAsync(database, projectId);
        var before = await ReadAsync(database, id);

        // Act
        foreach (var status in new[] { WorkItemStatus.Todo, WorkItemStatus.InProgress, WorkItemStatus.InReview,
            WorkItemStatus.Done, WorkItemStatus.Done, WorkItemStatus.Todo, WorkItemStatus.Cancelled, WorkItemStatus.Backlog })
            Assert.Equal(OperationStatus.Success, (await ChangeAsync(database, projectId, id, status)).Status);
        await using var read = database.OpenContext();
        var item = await read.WorkItems.SingleAsync();
        var events = await read.ChangeEvents.Where(change => change.Action == "StatusChanged").OrderBy(change => change.Id).ToListAsync();

        // Assert
        Assert.Equal(WorkItemStatus.Backlog, item.Status);
        Assert.Equal(8, item.Version);
        Assert.Equal(7, events.Count);
        Assert.All(events, change => Assert.Equal("member", change.ActorId));
        Assert.Equal(before.Number, item.Number);
        Assert.Equal(2, (await read.Projects.SingleAsync()).NextWorkItemNumber);
        Assert.Equal(3, (await read.Projects.SingleAsync()).Version);
        using var first = JsonDocument.Parse(events[0].DetailsJson);
        Assert.Equal((int)WorkItemStatus.Backlog, first.RootElement.GetProperty("Previous").GetInt32());
        Assert.Equal((int)WorkItemStatus.Todo, first.RootElement.GetProperty("Current").GetInt32());
        Assert.Equal(DateTimeKind.Utc, events[0].OccurredAtUtc.Kind);
    }

    [Fact]
    public async Task InvalidAndStaleChangesLeaveTrackedAndStoredStateIntact()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var id = await CreateAsync(database, projectId);

        // Act
        await using (var rejected = new WorkItemSession(database.OpenContext(), new TestUser("member")))
        {
            Assert.Equal(OperationStatus.Invalid, (await rejected.ChangeStatus.Handle(new(projectId, id, 1, WorkItemStatus.Done), default)).Status);
            Assert.Equal(WorkItemStatus.Backlog, (await rejected.Context.WorkItems.SingleAsync()).Status);
            Assert.Equal(0, await rejected.Context.SaveChangesAsync());
        }
        var unknown = await ChangeAsync(database, projectId, id, (WorkItemStatus)99);
        var saved = await ChangeAsync(database, projectId, id, WorkItemStatus.Todo);
        await using var staleSession = new WorkItemSession(database.OpenContext(), new TestUser("member"));
        var stale = await staleSession.ChangeStatus.Handle(new(projectId, id, 1, WorkItemStatus.Todo), default);
        var noOp = await ChangeAsync(database, projectId, id, WorkItemStatus.Todo);
        await using var read = database.OpenContext();

        // Assert
        Assert.Equal(OperationStatus.Invalid, unknown.Status);
        Assert.Equal(OperationStatus.Success, saved.Status);
        Assert.Equal(OperationStatus.Conflict, stale.Status);
        Assert.Equal(OperationStatus.Success, noOp.Status);
        Assert.Equal(WorkItemStatus.Todo, (await read.WorkItems.SingleAsync()).Status);
        Assert.Equal(2, (await read.WorkItems.SingleAsync()).Version);
        Assert.Single(await read.ChangeEvents.Where(change => change.Action == "StatusChanged").ToListAsync());
    }

    [Fact]
    public async Task CompletionAndReopeningRespectRealDescendantsWithoutCancellationCascade()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var epic = await CreateAsync(database, projectId, WorkItemType.Epic);
        var parent = await CreateAsync(database, projectId, epicId: epic);
        var child = await CreateAsync(database, projectId, WorkItemType.Subtask, parentId: parent);
        await AdvanceToReviewAsync(database, projectId, epic);
        await ChangeAsync(database, projectId, parent, WorkItemStatus.Cancelled);

        // Act
        var unfinishedGrandchild = await ChangeAsync(database, projectId, epic, WorkItemStatus.Done);
        Assert.Equal(WorkItemStatus.Backlog, (await ReadAsync(database, child)).Status);
        await ChangeAsync(database, projectId, child, WorkItemStatus.Cancelled);
        var epicDone = await ChangeAsync(database, projectId, epic, WorkItemStatus.Done);
        var childReopen = await ChangeAsync(database, projectId, child, WorkItemStatus.Backlog);
        var parentReopen = await ChangeAsync(database, projectId, parent, WorkItemStatus.Backlog);
        await ChangeAsync(database, projectId, epic, WorkItemStatus.Todo);
        Assert.Equal(OperationStatus.Success, (await ChangeAsync(database, projectId, child, WorkItemStatus.Backlog)).Status);
        Assert.Equal(OperationStatus.Success, (await ChangeAsync(database, projectId, parent, WorkItemStatus.Backlog)).Status);
        await AdvanceToReviewAsync(database, projectId, parent);
        var parentBlocked = await ChangeAsync(database, projectId, parent, WorkItemStatus.Done);
        await AdvanceToReviewAsync(database, projectId, child);
        await ChangeAsync(database, projectId, child, WorkItemStatus.Done);
        var parentDone = await ChangeAsync(database, projectId, parent, WorkItemStatus.Done);
        var underDoneParent = await ChangeAsync(database, projectId, child, WorkItemStatus.Todo);
        await ChangeAsync(database, projectId, parent, WorkItemStatus.Todo);
        var allowedReopen = await ChangeAsync(database, projectId, child, WorkItemStatus.Todo);

        // Assert
        Assert.Equal(OperationStatus.Invalid, unfinishedGrandchild.Status);
        Assert.Equal(OperationStatus.Success, epicDone.Status);
        Assert.Equal(OperationStatus.Invalid, childReopen.Status);
        Assert.Equal("Status", childReopen.Field);
        Assert.Equal(OperationStatus.Invalid, parentReopen.Status);
        Assert.Equal(OperationStatus.Invalid, parentBlocked.Status);
        Assert.Equal(OperationStatus.Success, parentDone.Status);
        Assert.Equal(OperationStatus.Invalid, underDoneParent.Status);
        Assert.Equal(OperationStatus.Success, allowedReopen.Status);
        Assert.Equal(WorkItemStatus.Todo, (await ReadAsync(database, child)).Status);
    }

    [Fact]
    public async Task CurrentScopeAndArchiveRulesAreCheckedBeforeStateMutation()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var id = await CreateAsync(database, projectId);
        int foreignProject, foreignWork;
        await using (var session = database.Open("other")) foreignProject = (await session.Create.Handle(new("CD", "Other", ""), default)).Value;
        await using (var session = new WorkItemSession(database.OpenContext(), new TestUser("other")))
            foreignWork = (await session.CreateWork.Handle(new(foreignProject, WorkItemType.Task, "Other", "", WorkItemPriority.Medium, null, null, null, null, []), default)).Value;

        // Act
        var outsider = await ChangeAsync(database, projectId, id, WorkItemStatus.Todo, "other");
        await using (var member = new WorkItemSession(database.OpenContext(), new TestUser("member")))
            Assert.Equal(OperationStatus.NotFound, (await member.ChangeStatus.Handle(new(projectId, foreignWork, 1, WorkItemStatus.Todo), default)).Status);
        await using (var owner = new WorkItemSession(database.OpenContext(), new TestUser("owner")))
            await owner.ArchiveWork.Handle(new(projectId, id, 1, true), default);
        var archivedWork = await ChangeAsync(database, projectId, id, WorkItemStatus.Todo);
        await using (var owner = new WorkItemSession(database.OpenContext(), new TestUser("owner")))
            await owner.ArchiveWork.Handle(new(projectId, id, 2, false), default);
        await using (var owner = database.Open()) await owner.Archive.Handle(new(projectId, 3, true), default);
        var archivedProject = await ChangeAsync(database, projectId, id, WorkItemStatus.Todo);
        await using (var owner = database.Open()) await owner.RemoveMember.Handle(new(projectId, 4, "member"), default);
        var revoked = await ChangeAsync(database, projectId, id, WorkItemStatus.Todo);
        await using var read = database.OpenContext();

        // Assert
        Assert.Equal(OperationStatus.NotFound, outsider.Status);
        Assert.Equal(OperationStatus.Invalid, archivedWork.Status);
        Assert.Equal(OperationStatus.Invalid, archivedProject.Status);
        Assert.Equal(OperationStatus.NotFound, revoked.Status);
        Assert.All(await read.WorkItems.ToListAsync(), work => Assert.Equal(WorkItemStatus.Backlog, work.Status));
        Assert.False(await read.ChangeEvents.AnyAsync(change => change.Action == "StatusChanged"));
    }

    [Fact]
    public async Task EventFailureRollsBackStatusVersionAndTimestamp()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var id = await CreateAsync(database, projectId);
        var before = await ReadAsync(database, id);

        // Act
        await using (var session = new WorkItemSession(database.OpenContext(new RejectWorkEvent()), new TestUser("member")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ChangeStatus.Handle(new(projectId, id, 1, WorkItemStatus.Todo), default));
        var after = await ReadAsync(database, id);
        await using var read = database.OpenContext();

        // Assert
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.False(await read.ChangeEvents.AnyAsync(change => change.Action == "StatusChanged"));
    }

    private static async Task<int> PrepareAsync(ProjectTestDatabase database)
    {
        await database.InitializeAsync();
        await using var session = database.Open();
        var projectId = (await session.Create.Handle(new("AB", "Project", ""), default)).Value;
        await session.AddMember.Handle(new(projectId, 1, "member@example.test"), default);
        return projectId;
    }

    private static async Task<int> CreateAsync(ProjectTestDatabase database, int projectId,
        WorkItemType type = WorkItemType.Task, int? parentId = null, int? epicId = null)
    {
        await using var session = new WorkItemSession(database.OpenContext(), new TestUser("owner"));
        var result = await session.CreateWork.Handle(new(projectId, type, "Task", "", WorkItemPriority.Medium, null, null, parentId, epicId, []), default);
        Assert.Equal(OperationStatus.Success, result.Status);
        return result.Value;
    }

    private static async Task<WorkItem> ReadAsync(ProjectTestDatabase database, int id)
    {
        await using var read = database.OpenContext();
        return (await read.WorkItems.FindAsync(id))!;
    }

    private static async Task<OperationResult<int>> ChangeAsync(ProjectTestDatabase database, int projectId, int id,
        WorkItemStatus status, string userId = "member")
    {
        var work = await ReadAsync(database, id);
        await using var session = new WorkItemSession(database.OpenContext(), new TestUser(userId));
        return await session.ChangeStatus.Handle(new(projectId, id, work.Version, status), default);
    }

    private static async Task AdvanceToReviewAsync(ProjectTestDatabase database, int projectId, int id)
    {
        foreach (var status in new[] { WorkItemStatus.Todo, WorkItemStatus.InProgress, WorkItemStatus.InReview })
            Assert.Equal(OperationStatus.Success, (await ChangeAsync(database, projectId, id, status)).Status);
    }
}
