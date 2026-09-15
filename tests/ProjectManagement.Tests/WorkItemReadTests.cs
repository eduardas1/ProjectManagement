using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.WorkItems;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Domain.Enums;
using ProjectManagement.Infrastructure.Repositories;

namespace ProjectManagement.Tests;

public class WorkItemReadTests
{
    [Fact]
    public async Task ScopedReadsShowEffectiveEpicBothLinkDirectionsAndPrivateChoices()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        int projectId, foreignProject, epicId, parentId, childId;
        await using (var session = database.Open())
        {
            projectId = (await session.Create.Handle(new("AB", "Shared", ""), default)).Value;
            await session.AddMember.Handle(new(projectId, 1, "member@example.test"), default);
            foreignProject = (await session.Create.Handle(new("CD", "Private", ""), default)).Value;
        }
        await using (var session = new WorkItemSession(database.OpenContext(), new TestUser("owner")))
        {
            epicId = (await session.CreateWork.Handle(New(projectId, WorkItemType.Epic), default)).Value;
            parentId = (await session.CreateWork.Handle(New(projectId) with { EpicId = epicId }, default)).Value;
            childId = (await session.CreateWork.Handle(New(projectId, WorkItemType.Subtask) with { ParentId = parentId }, default)).Value;
            await session.AddLink.Handle(new(projectId, parentId, 1, childId, WorkItemLinkKind.Blocks), default);
            await session.CreateLabel.Handle(new(projectId, "Shared label"), default);
            await session.CreateLabel.Handle(new(foreignProject, "Private label"), default);
            await session.CreateWork.Handle(New(foreignProject) with { Title = "Secret work" }, default);
        }
        await using var context = database.OpenContext();
        var member = new WorkItemReadRepository(context, new TestUser("member"));

        // Act
        var options = await member.GetOptionsAsync(projectId, default);
        var detail = await member.GetDetailsAsync(projectId, childId, default);
        var parent = await member.GetDetailsAsync(projectId, parentId, default);
        var list = await member.ListAsync(projectId, false, 1, default);

        // Assert
        Assert.NotNull(options);
        Assert.False(options.Project.IsOwner);
        Assert.Equal(2, options.Members.Count);
        Assert.Equal("Shared label", Assert.Single(options.Labels).Name);
        Assert.Equal(3, options.Items.Count);
        Assert.NotNull(detail);
        Assert.Equal(epicId, detail.Epic!.Id);
        Assert.Equal(parentId, detail.Parent!.Id);
        Assert.False(Assert.Single(detail.Connections).IsOutgoing);
        Assert.True(Assert.Single(parent!.Connections).IsOutgoing);
        Assert.Equal(parentId, Assert.Single(detail.Connections).TargetId);
        Assert.NotEmpty(detail.History);
        Assert.Equal(3, list!.Total);
        Assert.Null(await member.GetOptionsAsync(foreignProject, default));
        Assert.Null(await member.ListAsync(foreignProject, false, 1, default));
        Assert.Null(await member.ListLabelsAsync(foreignProject, default));
        Assert.Null(await member.GetDetailsAsync(foreignProject, childId, default));
        Assert.Null(await member.GetDetailsAsync(projectId, int.MaxValue, default));
    }

    [Fact]
    public async Task RevocationImmediatelyInvalidatesPreviouslyUsedReadRepository()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        int projectId, workId;
        await using (var session = database.Open())
        {
            projectId = (await session.Create.Handle(new("AB", "Shared", ""), default)).Value;
            await session.AddMember.Handle(new(projectId, 1, "member@example.test"), default);
        }
        await using (var session = new WorkItemSession(database.OpenContext(), new TestUser("owner")))
            workId = (await session.CreateWork.Handle(New(projectId), default)).Value;
        await using var context = database.OpenContext();
        var repository = new WorkItemReadRepository(context, new TestUser("member"));
        Assert.NotNull(await repository.GetDetailsAsync(projectId, workId, default));
        Assert.NotNull(await repository.GetOptionsAsync(projectId, default));

        // Act
        await using (var owner = database.Open())
            Assert.Equal(OperationStatus.Success, (await owner.RemoveMember.Handle(new(projectId, 3, "member"), default)).Status);

        // Assert
        Assert.Null(await repository.GetDetailsAsync(projectId, workId, default));
        Assert.Null(await repository.GetOptionsAsync(projectId, default));
        Assert.Null(await repository.ListAsync(projectId, false, 1, default));
        Assert.Null(await repository.ListLabelsAsync(projectId, default));
    }

    [Fact]
    public async Task ListsPageDeterministicallyAndKeepArchivedWorkExplicit()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        int projectId;
        await using (var session = database.Open()) projectId = (await session.Create.Handle(new("AB", "Project", ""), default)).Value;
        await using (var context = database.OpenContext())
        {
            context.WorkItems.AddRange(Enumerable.Range(1, 23).Select(number => new WorkItem
            {
                ProjectId = projectId, Number = number, Type = WorkItemType.Task, Title = "Task " + number,
                CreatedById = "owner", IsArchived = number == 23, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            }));
            await context.SaveChangesAsync();
        }
        await using var read = database.OpenContext();
        var repository = new WorkItemReadRepository(read, new TestUser("owner"));

        // Act
        var first = await repository.ListAsync(projectId, false, 0, default);
        var last = await repository.ListAsync(projectId, false, int.MaxValue, default);
        var archived = await repository.ListAsync(projectId, true, 1, default);
        var denied = await new WorkItemReadRepository(read, new TestUser("other")).ListAsync(projectId, true, 1, default);

        // Assert
        Assert.Equal(22, first!.Total);
        Assert.Equal(Enumerable.Range(1, 20), first.Items.Select(item => item.Number));
        Assert.Equal(2, last!.Page);
        Assert.Equal(new[] { 21, 22 }, last.Items.Select(item => item.Number));
        Assert.Equal(23, Assert.Single(archived!.Items).Number);
        Assert.Null(denied);
    }

    [Fact]
    public async Task BoardAndExactListsAgreeOnSavedStatesCountsAndArchiveBounds()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        int projectId, foreignProject;
        await using (var session = database.Open())
        {
            projectId = (await session.Create.Handle(new("AB", "Shared", ""), default)).Value;
            await session.AddMember.Handle(new(projectId, 1, "member@example.test"), default);
            foreignProject = (await session.Create.Handle(new("CD", "Private", ""), default)).Value;
        }
        await using (var setup = database.OpenContext())
        {
            setup.WorkItems.AddRange(Enumerable.Range(1, 28).Select(number => new WorkItem
            {
                ProjectId = projectId, Number = number, Title = "Work " + number, CreatedById = "owner",
                Type = number == 1 ? WorkItemType.Epic : WorkItemType.Task, IsArchived = number == 28,
                Status = number <= 22 || number == 28 ? WorkItemStatus.Todo : (number == 27 ? WorkItemStatus.Backlog : (WorkItemStatus)(number - 21)),
                Version = number + 10, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            }));
            setup.WorkItems.Add(new WorkItem { ProjectId = foreignProject, Number = 1, Title = "Secret", CreatedById = "owner",
                Status = WorkItemStatus.Todo, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            await setup.SaveChangesAsync();
        }
        await using var read = database.OpenContext();
        var repository = new WorkItemReadRepository(read, new TestUser("member"));

        // Act
        var board = await repository.GetBoardAsync(projectId, default);
        var todo = await repository.ListAsync(projectId, false, 1, default, WorkItemStatus.Todo);
        var last = await repository.ListAsync(projectId, false, int.MaxValue, default, WorkItemStatus.Todo);
        var archived = await repository.ListAsync(projectId, true, 1, default, WorkItemStatus.Todo);
        var all = await repository.ListAsync(projectId, false, 1, default);

        // Assert
        Assert.NotNull(board);
        Assert.Equal(new[] { WorkItemStatus.Todo, WorkItemStatus.InProgress, WorkItemStatus.InReview, WorkItemStatus.Done }, board.Columns.Select(column => column.Status));
        Assert.Equal(new[] { 22, 1, 1, 1 }, board.Columns.Select(column => column.Total));
        Assert.Equal(20, board.Columns[0].Items.Count);
        Assert.Equal(Enumerable.Range(1, 20), board.Columns[0].Items.Select(item => item.Number));
        Assert.Equal(WorkItemType.Epic, board.Columns[0].Items[0].Type);
        Assert.Equal(todo!.Items, board.Columns[0].Items);
        Assert.Equal(Enumerable.Range(11, 20).Select(version => (long)version), board.Columns[0].Items.Select(item => item.Version));
        Assert.Equal(22, todo.Total);
        Assert.Equal(WorkItemStatus.Todo, todo.Status);
        Assert.Equal(2, last!.Page);
        Assert.Equal(new[] { 21, 22 }, last.Items.Select(item => item.Number));
        Assert.Equal(28, Assert.Single(archived!.Items).Number);
        Assert.Equal(27, all!.Total);
        Assert.Equal(23, board.Columns.Sum(column => column.Items.Count));
        Assert.Equal(23, board.Columns.SelectMany(column => column.Items).Select(item => item.Id).Distinct().Count());
        Assert.Null(await repository.GetBoardAsync(foreignProject, default));
        Assert.Null(await repository.ListAsync(foreignProject, false, 1, default, WorkItemStatus.Todo));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.ListAsync(projectId, false, 1, default, (WorkItemStatus)99));
    }

    [Fact]
    public async Task BoardReloadFollowsStatusCommandAndCurrentMembershipEvenOnArchivedProjects()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        int projectId, workId;
        await using (var session = database.Open())
        {
            projectId = (await session.Create.Handle(new("AB", "Shared", ""), default)).Value;
            await session.AddMember.Handle(new(projectId, 1, "member@example.test"), default);
        }
        await using (var session = new WorkItemSession(database.OpenContext(), new TestUser("owner")))
            workId = (await session.CreateWork.Handle(New(projectId), default)).Value;
        await using var context = database.OpenContext();
        var repository = new WorkItemReadRepository(context, new TestUser("member"));
        Assert.All((await repository.GetBoardAsync(projectId, default))!.Columns, column => Assert.Empty(column.Items));
        Assert.Single((await repository.ListAsync(projectId, false, 1, default, WorkItemStatus.Backlog))!.Items);

        // Act
        await using (var session = new WorkItemSession(database.OpenContext(), new TestUser("member")))
            Assert.Equal(OperationStatus.Success, (await session.ChangeStatus.Handle(new(projectId, workId, 1, WorkItemStatus.Todo), default)).Status);
        var moved = await repository.GetBoardAsync(projectId, default);
        await using (var session = database.Open()) await session.Archive.Handle(new(projectId, 3, true), default);
        var archivedProject = await repository.GetBoardAsync(projectId, default);
        await using (var session = database.Open()) await session.RemoveMember.Handle(new(projectId, 4, "member"), default);
        var revoked = await repository.GetBoardAsync(projectId, default);

        // Assert
        Assert.Equal(workId, Assert.Single(moved!.Columns[0].Items).Id);
        Assert.All(moved.Columns.Skip(1), column => Assert.Empty(column.Items));
        Assert.Empty((await new WorkItemReadRepository(context, new TestUser("owner")).ListAsync(projectId, false, 1, default, WorkItemStatus.Backlog))!.Items);
        Assert.True(archivedProject!.Project.IsArchived);
        Assert.Single(archivedProject.Columns[0].Items);
        Assert.Null(revoked);
        Assert.Null(await repository.ListAsync(projectId, false, 1, default, WorkItemStatus.Todo));
    }

    private static CreateWorkItemCommand New(int projectId, WorkItemType type = WorkItemType.Task)
        => new(projectId, type, "Task", "", WorkItemPriority.Medium, null, null, null, null, []);
}
