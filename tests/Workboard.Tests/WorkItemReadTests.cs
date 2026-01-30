using Microsoft.EntityFrameworkCore;
using Workboard.Application.Common;
using Workboard.Application.WorkItems;
using Workboard.Domain.Entities;
using Workboard.Domain.Enums;
using Workboard.Infrastructure.Repositories;

namespace Workboard.Tests;

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

    private static CreateWorkItemCommand New(int projectId, WorkItemType type = WorkItemType.Task)
        => new(projectId, type, "Task", "", WorkItemPriority.Medium, null, null, null, null, []);
}
