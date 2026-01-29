using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workboard.Application.Common;
using Workboard.Application.Labels;
using Workboard.Application.WorkItems;
using Workboard.Domain.Entities;
using Workboard.Domain.Enums;
using Workboard.Infrastructure.Data;
using Workboard.Infrastructure.Repositories;

namespace Workboard.Tests;

public class WorkItemCommandTests
{
    [Fact]
    public async Task MemberCreatesNumberedHierarchyWithLabelsAssignmentAndDeadline()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var labelId = (await RunAsync(database, session => session.CreateLabel.Handle(new(projectId, " Release "), default), "member")).Value;

        // Act
        var epicId = (await CreateAsync(database, projectId, WorkItemType.Epic)).Value;
        var parentId = (await CreateAsync(database, projectId, epicId: epicId, assigneeId: "member", labelIds: [labelId])).Value;
        var childId = (await CreateAsync(database, projectId, WorkItemType.Subtask, parentId: parentId, userId: "member")).Value;
        await using var read = database.OpenContext();
        var work = await read.WorkItems.OrderBy(item => item.Number).ToListAsync();

        // Assert
        Assert.Equal(new[] { 1, 2, 3 }, work.Select(item => item.Number));
        Assert.All(work, item => Assert.Equal(WorkItemStatus.Backlog, item.Status));
        Assert.Equal("member", work[1].AssigneeId);
        Assert.Equal(new DateOnly(2026, 11, 1), work[1].DueDate);
        Assert.Equal(epicId, work[1].EpicId);
        Assert.Equal(parentId, work[2].ParentId);
        Assert.Null(work[2].EpicId);
        Assert.Equal(childId, work[2].Id);
        Assert.Equal("member", work[2].CreatedById);
        Assert.Equal(labelId, (await read.WorkItemLabels.SingleAsync()).LabelId);
        Assert.Equal(4, (await read.Projects.SingleAsync()).NextWorkItemNumber);
        Assert.Equal(3, await read.ChangeEvents.CountAsync(change => change.Action == "WorkCreated"));
        Assert.All(work, item => Assert.Equal(DateTimeKind.Utc, item.CreatedAtUtc.Kind));
    }

    [Fact]
    public async Task InvalidFieldsAndForeignReferencesLeaveNumberAndHistoryUntouched()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        int foreignProject;
        await using (var session = database.Open()) foreignProject = (await session.Create.Handle(new("CD", "Other project", ""), default)).Value;
        var foreignLabel = (await RunAsync(database, session => session.CreateLabel.Handle(new(foreignProject, "Foreign"), default))).Value;
        var original = new CreateWorkItemCommand(projectId, WorkItemType.Task, "Task", "", WorkItemPriority.Medium, null, null, null, null, []);
        var invalid = new[] { original with { Title = " " }, original with { Description = new string('x', 20001) },
            original with { Type = (WorkItemType)99 }, original with { Priority = (WorkItemPriority)99 },
            original with { AssigneeId = "other" }, original with { LabelIds = new[] { foreignLabel } },
            original with { Type = WorkItemType.Subtask } };

        // Act
        foreach (var request in invalid)
            Assert.Equal(OperationStatus.Invalid, (await RunAsync(database, session => session.CreateWork.Handle(request, default))).Status);
        var denied = await CreateAsync(database, projectId, userId: "other");
        await using var read = database.OpenContext();

        // Assert
        Assert.Equal(OperationStatus.NotFound, denied.Status);
        Assert.Empty(await read.WorkItems.ToListAsync());
        Assert.Equal(1, (await read.Projects.FindAsync(projectId))!.NextWorkItemNumber);
        Assert.False(await read.ChangeEvents.AnyAsync(change => change.WorkItemId != null));
    }

    [Fact]
    public async Task UpdateReplacesLabelsAtomicallyAndChecksStaleBeforeMatchingNoOp()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var first = (await RunAsync(database, session => session.CreateLabel.Handle(new(projectId, "First"), default))).Value;
        var second = (await RunAsync(database, session => session.CreateLabel.Handle(new(projectId, "Second"), default))).Value;
        var id = (await CreateAsync(database, projectId, labelIds: [first])).Value;
        var command = new UpdateWorkItemCommand(projectId, id, 1, " Changed ", "Notes", WorkItemPriority.High, "member", null, [second]);

        // Act
        var saved = await RunAsync(database, session => session.UpdateWork.Handle(command, default), "member");
        var stale = await RunAsync(database, session => session.UpdateWork.Handle(command, default));
        var noOp = await RunAsync(database, session => session.UpdateWork.Handle(command with { ExpectedVersion = 2 }, default));
        var bad = await RunAsync(database, session => session.UpdateWork.Handle(command with { ExpectedVersion = 2, AssigneeId = "other", Title = "Bad" }, default));
        await using var read = database.OpenContext();
        var work = await read.WorkItems.SingleAsync();

        // Assert
        Assert.Equal(OperationStatus.Success, saved.Status);
        Assert.Equal(OperationStatus.Conflict, stale.Status);
        Assert.Equal(OperationStatus.Success, noOp.Status);
        Assert.Equal(OperationStatus.Invalid, bad.Status);
        Assert.Equal("Changed", work.Title);
        Assert.Equal(2, work.Version);
        Assert.Equal(1, work.Number);
        Assert.Equal(WorkItemType.Task, work.Type);
        Assert.Equal(second, (await read.WorkItemLabels.SingleAsync()).LabelId);
        Assert.Single(await read.ChangeEvents.Where(change => change.Action == "WorkUpdated").ToListAsync());
    }

    [Fact]
    public async Task FailedCreationRollsBackAllocatedIdNumberJoinsAndProjectVersion()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var labelId = (await RunAsync(database, session => session.CreateLabel.Handle(new(projectId, "Label"), default))).Value;
        var request = new CreateWorkItemCommand(projectId, WorkItemType.Task, "Task", "", WorkItemPriority.Medium, null, null, null, null, [labelId]);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(database,
            session => session.CreateWork.Handle(request, default), interceptor: new RejectWorkEvent()));
        await using (var read = database.OpenContext())
        {
            Assert.Empty(await read.WorkItems.ToListAsync());
            Assert.Empty(await read.WorkItemLabels.ToListAsync());
            Assert.Equal(1, (await read.Projects.SingleAsync()).NextWorkItemNumber);
            Assert.Equal(2, (await read.Projects.SingleAsync()).Version);
        }
        var saved = await RunAsync(database, session => session.CreateWork.Handle(request, default));
        await using var final = database.OpenContext();

        // Assert
        Assert.Equal(OperationStatus.Success, saved.Status);
        Assert.Equal(1, (await final.WorkItems.SingleAsync()).Number);
        Assert.Single(await final.ChangeEvents.Where(change => change.WorkItemId != null).ToListAsync());
    }

    [Fact]
    public async Task LabelsHaveCaseInsensitiveNamesVersionedRenameAndOwnerUnusedDeletion()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var labelId = (await RunAsync(database, session => session.CreateLabel.Handle(new(projectId, " Release "), default), "member")).Value;
        var workId = (await CreateAsync(database, projectId, labelIds: [labelId])).Value;

        // Act
        var duplicate = await RunAsync(database, session => session.CreateLabel.Handle(new(projectId, "release"), default));
        var renamed = await RunAsync(database, session => session.RenameLabel.Handle(new(projectId, labelId, 1, "Ready"), default), "member");
        var stale = await RunAsync(database, session => session.RenameLabel.Handle(new(projectId, labelId, 1, "Ready"), default));
        var denied = await RunAsync(database, session => session.DeleteLabel.Handle(new(projectId, labelId, 2), default), "member");
        var inUse = await RunAsync(database, session => session.DeleteLabel.Handle(new(projectId, labelId, 2), default));
        await RunAsync(database, session => session.UpdateWork.Handle(new(projectId, workId, 1, "Task", "", WorkItemPriority.Medium, null, null, []), default));
        var deleted = await RunAsync(database, session => session.DeleteLabel.Handle(new(projectId, labelId, 2), default));

        // Assert
        Assert.Equal(OperationStatus.Invalid, duplicate.Status);
        Assert.Equal(OperationStatus.Success, renamed.Status);
        Assert.Equal(OperationStatus.Conflict, stale.Status);
        Assert.Equal(OperationStatus.NotFound, denied.Status);
        Assert.Equal(OperationStatus.Invalid, inUse.Status);
        Assert.Equal(OperationStatus.Success, deleted.Status);
        await using var read = database.OpenContext();
        Assert.Empty(await read.Labels.ToListAsync());
        Assert.Empty(await read.WorkItemLabels.ToListAsync());
        Assert.Single(await read.ChangeEvents.Where(change => change.Action == "LabelDeleted").ToListAsync());
    }

    [Fact]
    public async Task HierarchyRejectsForeignWrongArchivedAndDoneAncestorsIncludingMovingSubtree()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var epicId = (await CreateAsync(database, projectId, WorkItemType.Epic)).Value;
        var parentId = (await CreateAsync(database, projectId)).Value;
        var childId = (await CreateAsync(database, projectId, WorkItemType.Subtask, parentId: parentId)).Value;
        await SetStatusAsync(database, epicId, WorkItemStatus.Done);
        await SetStatusAsync(database, parentId, WorkItemStatus.Cancelled);

        // Act
        var moving = await RunAsync(database, session => session.Hierarchy.Handle(new(projectId, parentId, 1, null, epicId), default));
        var nested = await RunAsync(database, session => session.Hierarchy.Handle(new(projectId, childId, 1, childId, null), default));
        var wrongType = await CreateAsync(database, projectId, WorkItemType.Subtask, parentId: epicId);
        var doneChild = await CreateAsync(database, projectId, epicId: epicId);
        await SetStatusAsync(database, epicId, WorkItemStatus.Todo);
        var valid = await RunAsync(database, session => session.Hierarchy.Handle(new(projectId, parentId, 1, null, epicId), default));
        var stale = await RunAsync(database, session => session.Hierarchy.Handle(new(projectId, parentId, 1, null, epicId), default));
        await RunAsync(database, session => session.ArchiveWork.Handle(new(projectId, childId, 1, true), default));
        await RunAsync(database, session => session.ArchiveWork.Handle(new(projectId, parentId, 2, true), default));
        var archivedParent = await CreateAsync(database, projectId, WorkItemType.Subtask, parentId: parentId);
        int foreignProject;
        await using (var session = database.Open()) foreignProject = (await session.Create.Handle(new("CD", "Other", ""), default)).Value;
        var foreignEpic = (await CreateAsync(database, foreignProject, WorkItemType.Epic)).Value;
        var foreign = await CreateAsync(database, projectId, epicId: foreignEpic);

        // Assert
        Assert.Equal(OperationStatus.Invalid, moving.Status);
        Assert.Equal(OperationStatus.Invalid, nested.Status);
        Assert.Equal(OperationStatus.Invalid, wrongType.Status);
        Assert.Equal(OperationStatus.Invalid, doneChild.Status);
        Assert.Equal(OperationStatus.Success, valid.Status);
        Assert.Equal(OperationStatus.Conflict, stale.Status);
        Assert.Equal(OperationStatus.Invalid, archivedParent.Status);
        Assert.Equal(OperationStatus.Invalid, foreign.Status);
        await using var read = database.OpenContext();
        Assert.Equal(epicId, (await read.WorkItems.FindAsync(parentId))!.EpicId);
        Assert.Null((await read.WorkItems.FindAsync(childId))!.EpicId);
    }

    [Fact]
    public async Task BlockingCycleIncludesArchivedIntermediaryAndRelationsKeepIndependentKinds()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var first = (await CreateAsync(database, projectId)).Value;
        var middle = (await CreateAsync(database, projectId)).Value;
        var last = (await CreateAsync(database, projectId)).Value;
        await AddLinkAsync(database, projectId, first, middle, WorkItemLinkKind.Blocks);
        await AddLinkAsync(database, projectId, middle, last, WorkItemLinkKind.Blocks);
        await RunAsync(database, async session => await session.ArchiveWork.Handle(new(projectId, middle, await VersionAsync(database, middle), true), default));

        // Act
        var cycle = await AddLinkAsync(database, projectId, last, first, WorkItemLinkKind.Blocks);
        var separate = await AddLinkAsync(database, projectId, last, first, WorkItemLinkKind.Duplicates);
        var duplicateCycle = await AddLinkAsync(database, projectId, first, last, WorkItemLinkKind.Duplicates);
        var archivedTarget = await AddLinkAsync(database, projectId, first, middle, WorkItemLinkKind.Duplicates);
        await using var read = database.OpenContext();

        // Assert
        Assert.Equal(OperationStatus.Invalid, cycle.Status);
        Assert.Equal(OperationStatus.Success, separate.Status);
        Assert.Equal(OperationStatus.Invalid, duplicateCycle.Status);
        Assert.Equal(OperationStatus.Invalid, archivedTarget.Status);
        Assert.Equal(3, await read.WorkItemLinks.CountAsync());
        Assert.All(await read.WorkItems.ToListAsync(), item => Assert.Equal(WorkItemStatus.Backlog, item.Status));
    }

    [Fact]
    public async Task LinkChangesBothEndpointsRejectsSecondCanonicalAndAbsentOrStaleRemoval()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var source = (await CreateAsync(database, projectId)).Value;
        var target = (await CreateAsync(database, projectId)).Value;
        var other = (await CreateAsync(database, projectId)).Value;

        // Act
        var added = await AddLinkAsync(database, projectId, source, target, WorkItemLinkKind.Duplicates);
        var second = await AddLinkAsync(database, projectId, source, other, WorkItemLinkKind.Duplicates);
        var self = await AddLinkAsync(database, projectId, source, source, WorkItemLinkKind.Blocks);
        var stale = await RunAsync(database, session => session.RemoveLink.Handle(new(projectId, source, 1, target, WorkItemLinkKind.Duplicates), default));
        var removed = await RunAsync(database, session => session.RemoveLink.Handle(new(projectId, source, 2, target, WorkItemLinkKind.Duplicates), default));
        var absent = await RunAsync(database, session => session.RemoveLink.Handle(new(projectId, source, 3, target, WorkItemLinkKind.Duplicates), default));
        await using var read = database.OpenContext();

        // Assert
        Assert.Equal(OperationStatus.Success, added.Status);
        Assert.Equal(OperationStatus.Invalid, second.Status);
        Assert.Equal(OperationStatus.Invalid, self.Status);
        Assert.Equal(OperationStatus.Conflict, stale.Status);
        Assert.Equal(OperationStatus.Success, removed.Status);
        Assert.Equal(OperationStatus.Invalid, absent.Status);
        Assert.Equal(3, (await read.WorkItems.FindAsync(source))!.Version);
        Assert.Equal(3, (await read.WorkItems.FindAsync(target))!.Version);
        Assert.Equal(1, (await read.WorkItems.FindAsync(other))!.Version);
        Assert.Empty(await read.WorkItemLinks.ToListAsync());
        Assert.Equal(4, await read.ChangeEvents.CountAsync(change => change.Action == "LinkAdded" || change.Action == "LinkRemoved"));
    }

    [Fact]
    public async Task FailedLinkSaveLeavesBothVersionsAndGraphUntouched()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var source = (await CreateAsync(database, projectId)).Value;
        var target = (await CreateAsync(database, projectId)).Value;

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(database,
            session => session.AddLink.Handle(new(projectId, source, 1, target, WorkItemLinkKind.Blocks), default), interceptor: new RejectWorkEvent()));
        await using var read = database.OpenContext();

        // Assert
        Assert.Empty(await read.WorkItemLinks.ToListAsync());
        Assert.All(await read.WorkItems.ToListAsync(), item => Assert.Equal(1, item.Version));
        Assert.False(await read.ChangeEvents.AnyAsync(change => change.Action == "LinkAdded"));
    }

    [Fact]
    public async Task RevocationOnArchivedProjectClearsOnlyActiveAssignmentsAndRestoreClearsOldAssignee()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var active = (await CreateAsync(database, projectId, assigneeId: "member")).Value;
        var archived = (await CreateAsync(database, projectId, assigneeId: "member")).Value;
        await RunAsync(database, session => session.ArchiveWork.Handle(new(projectId, archived, 1, true), default));
        await RunAsync(database, async session => await session.Archive.Handle(new(projectId, await ProjectVersionAsync(database, projectId), true), default));

        // Act
        var revoked = await RunAsync(database, async session => await session.RemoveMember.Handle(new(projectId, await ProjectVersionAsync(database, projectId), "member"), default));
        await using (var read = database.OpenContext())
        {
            Assert.Null((await read.WorkItems.FindAsync(active))!.AssigneeId);
            Assert.Equal("member", (await read.WorkItems.FindAsync(archived))!.AssigneeId);
            Assert.False(await read.ProjectMembers.AnyAsync(member => member.ProjectId == projectId && member.UserId == "member"));
        }
        var denied = await RunAsync(database, session => session.UpdateWork.Handle(new(projectId, active, 2, "Bad", "", WorkItemPriority.Medium, null, null, []), default), "member");
        await RunAsync(database, async session => await session.Archive.Handle(new(projectId, await ProjectVersionAsync(database, projectId), false), default));
        var restored = await RunAsync(database, session => session.ArchiveWork.Handle(new(projectId, archived, 2, false), default));
        await using var final = database.OpenContext();

        // Assert
        Assert.Equal(OperationStatus.Success, revoked.Status);
        Assert.Equal(OperationStatus.NotFound, denied.Status);
        Assert.Equal(OperationStatus.Success, restored.Status);
        Assert.Null((await final.WorkItems.FindAsync(archived))!.AssigneeId);
        Assert.Equal(2, (await final.WorkItems.FindAsync(active))!.Version);
        Assert.Equal(3, (await final.WorkItems.FindAsync(archived))!.Version);
        Assert.Single(await final.ChangeEvents.Where(change => change.Action == "AssigneeCleared").ToListAsync());
    }

    [Fact]
    public async Task FailedRevocationRollsBackMembershipAssignmentAndAllEvents()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var workId = (await CreateAsync(database, projectId, assigneeId: "member")).Value;
        var version = await ProjectVersionAsync(database, projectId);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(database,
            session => session.RemoveMember.Handle(new(projectId, version, "member"), default), interceptor: new RejectWorkEvent()));
        await using var read = database.OpenContext();

        // Assert
        Assert.Equal("member", (await read.WorkItems.SingleAsync()).AssigneeId);
        Assert.Equal(1, (await read.WorkItems.SingleAsync()).Version);
        Assert.True(await read.ProjectMembers.AnyAsync(member => member.ProjectId == projectId && member.UserId == "member"));
        Assert.Equal(version, (await read.Projects.SingleAsync()).Version);
        Assert.False(await read.ChangeEvents.AnyAsync(change => change.Action == "AssigneeCleared" || change.Action == "MemberRemoved"));
    }

    [Fact]
    public async Task OwnerArchivesChildrenExplicitlyAndRestorationChecksCurrentAncestors()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);
        var parentId = (await CreateAsync(database, projectId)).Value;
        var childId = (await CreateAsync(database, projectId, WorkItemType.Subtask, parentId: parentId)).Value;

        // Act
        var denied = await RunAsync(database, session => session.ArchiveWork.Handle(new(projectId, childId, 1, true), default), "member");
        var hasChild = await RunAsync(database, session => session.ArchiveWork.Handle(new(projectId, parentId, 1, true), default));
        var childArchive = await RunAsync(database, session => session.ArchiveWork.Handle(new(projectId, childId, 1, true), default));
        var parentArchive = await RunAsync(database, session => session.ArchiveWork.Handle(new(projectId, parentId, 1, true), default));
        var cannotRestore = await RunAsync(database, session => session.ArchiveWork.Handle(new(projectId, childId, 2, false), default));
        await RunAsync(database, session => session.ArchiveWork.Handle(new(projectId, parentId, 2, false), default));
        await SetStatusAsync(database, parentId, WorkItemStatus.Done);
        var doneParent = await RunAsync(database, session => session.ArchiveWork.Handle(new(projectId, childId, 2, false), default));

        // Assert
        Assert.Equal(OperationStatus.NotFound, denied.Status);
        Assert.Equal(OperationStatus.Invalid, hasChild.Status);
        Assert.Equal(OperationStatus.Success, childArchive.Status);
        Assert.Equal(OperationStatus.Success, parentArchive.Status);
        Assert.Equal(OperationStatus.Invalid, cannotRestore.Status);
        Assert.Equal(OperationStatus.Invalid, doneParent.Status);
        await using var read = database.OpenContext();
        Assert.True((await read.WorkItems.FindAsync(childId))!.IsArchived);
        Assert.Equal(2, (await read.WorkItems.FindAsync(childId))!.Version);
    }

    [Fact]
    public async Task ConcurrentWritersKeepDistinctNumbersAndFailedAttemptCanBeSubmittedAgain()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var projectId = await PrepareAsync(database);

        // Act
        var attempts = await Task.WhenAll(Task.Run(() => CreateAsync(database, projectId)),
            Task.Run(() => CreateAsync(database, projectId, userId: "member")));
        foreach (var busy in attempts.Where(result => result.Status == OperationStatus.Busy))
            Assert.Equal(OperationStatus.Success, (await CreateAsync(database, projectId)).Status);
        await using var read = database.OpenContext();

        // Assert
        Assert.Contains(attempts, result => result.Status == OperationStatus.Success);
        Assert.All(attempts, result => Assert.Contains(result.Status, new[] { OperationStatus.Success, OperationStatus.Busy }));
        Assert.Equal(new[] { 1, 2 }, await read.WorkItems.OrderBy(item => item.Number).Select(item => item.Number).ToArrayAsync());
        Assert.Equal(3, (await read.Projects.SingleAsync()).NextWorkItemNumber);
        Assert.Equal(2, await read.ChangeEvents.CountAsync(change => change.Action == "WorkCreated"));
    }

    private static async Task<int> PrepareAsync(ProjectTestDatabase database)
    {
        await database.InitializeAsync();
        await using var session = database.Open();
        var id = (await session.Create.Handle(new("AB", "Project", ""), default)).Value;
        Assert.Equal(OperationStatus.Success, (await session.AddMember.Handle(new(id, 1, "member@example.test"), default)).Status);
        return id;
    }

    private static async Task<OperationResult<int>> RunAsync(ProjectTestDatabase database,
        Func<WorkItemSession, Task<OperationResult<int>>> action, string userId = "owner", SaveChangesInterceptor? interceptor = null)
    {
        await using var session = new WorkItemSession(database.OpenContext(interceptor), new TestUser(userId));
        return await action(session);
    }

    private static Task<OperationResult<int>> CreateAsync(ProjectTestDatabase database, int projectId,
        WorkItemType type = WorkItemType.Task, int? parentId = null, int? epicId = null,
        string? assigneeId = null, int[]? labelIds = null, string userId = "owner")
        => RunAsync(database, session => session.CreateWork.Handle(new(projectId, type, "Task", "", WorkItemPriority.Medium,
            assigneeId, new DateOnly(2026, 11, 1), parentId, epicId, labelIds ?? []), default), userId);

    private static async Task<OperationResult<int>> AddLinkAsync(ProjectTestDatabase database, int projectId,
        int source, int target, WorkItemLinkKind kind)
        => await RunAsync(database, async session => await session.AddLink.Handle(new(projectId, source,
            await VersionAsync(database, source), target, kind), default));

    private static async Task<long> VersionAsync(ProjectTestDatabase database, int workItemId)
    {
        await using var context = database.OpenContext();
        return (await context.WorkItems.FindAsync(workItemId))!.Version;
    }

    private static async Task<long> ProjectVersionAsync(ProjectTestDatabase database, int projectId)
    {
        await using var context = database.OpenContext();
        return (await context.Projects.FindAsync(projectId))!.Version;
    }

    private static async Task SetStatusAsync(ProjectTestDatabase database, int workItemId, WorkItemStatus status)
    {
        await using var context = database.OpenContext();
        (await context.WorkItems.FindAsync(workItemId))!.Status = status;
        await context.SaveChangesAsync();
    }
}

internal class WorkItemSession(ApplicationDbContext context, TestUser user) : ProjectSession(context, user)
{
    private WorkItemRepository Works => new(Context);
    private LabelRepository Labels => new(Context);
    private WorkItemValidation Validation => new(Works, Repository, Labels);
    public CreateWorkItemHandler CreateWork => new(Works, Repository, Access, Unit, Validation, User, TimeProvider.System);
    public UpdateWorkItemHandler UpdateWork => new(Works, Repository, Access, Unit, Validation, User, TimeProvider.System);
    public CreateLabelHandler CreateLabel => new(Labels, Repository, Access, Unit, User, TimeProvider.System);
    public RenameLabelHandler RenameLabel => new(Labels, Repository, Access, Unit, User, TimeProvider.System);
    public DeleteLabelHandler DeleteLabel => new(Labels, Repository, Access, Unit, User, TimeProvider.System);
    public ChangeWorkItemHierarchyHandler Hierarchy => new(Works, Repository, Access, Unit, Validation, User, TimeProvider.System);
    public SetWorkItemArchiveHandler ArchiveWork => new(Works, Repository, Access, Unit, Validation, User, TimeProvider.System);
    public AddWorkItemLinkHandler AddLink => new(Works, Repository, Access, Unit, User, TimeProvider.System);
    public RemoveWorkItemLinkHandler RemoveLink => new(Works, Repository, Access, Unit, User, TimeProvider.System);
}

internal class RejectWorkEvent : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<ChangeEvent>().Any(entry => entry.State == EntityState.Added && entry.Entity.WorkItemId != null))
            throw new InvalidOperationException("Simulated work-event failure.");
        return new ValueTask<InterceptionResult<int>>(result);
    }
}
