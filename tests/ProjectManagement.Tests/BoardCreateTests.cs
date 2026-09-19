using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Application.WorkItems;
using ProjectManagement.Domain.Enums;
using ProjectManagement.Infrastructure.Repositories;

namespace ProjectManagement.Tests;

public class BoardCreateTests
{
    [Fact]
    public async Task ColumnCreationUsesTheRealWorkflowAndRejectsAnInvalidTargetBeforeCreating()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        await using var session = database.Open();
        var project = (await session.Create.Handle(new("BD", "Board", ""), default)).Value;
        using var provider = Services(session).BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();
        var create = new CreateWorkItemCommand(project, WorkItemType.Task, "Prepare release", "",
            WorkItemPriority.Medium, null, null, null, null, []);

        // Act
        var saved = await sender.Send(new CreateBoardWorkItemCommand(create, WorkItemStatus.Done));
        var invalid = await sender.Send(new CreateBoardWorkItemCommand(create, (WorkItemStatus)99));

        // Assert
        Assert.Equal(OperationStatus.Success, saved.Status);
        Assert.True(saved.Value!.TargetReached);
        Assert.Equal(OperationStatus.Invalid, invalid.Status);
        var work = await session.Context.WorkItems.SingleAsync();
        Assert.Equal(saved.Value.Id, work.Id);
        Assert.Equal(WorkItemStatus.Done, work.Status);
        Assert.Equal(5, work.Version);
        Assert.Equal(4, await session.Context.ChangeEvents.CountAsync(change => change.Action == "StatusChanged"));
    }

    [Fact]
    public async Task AFailedFollowupReturnsTheCreatedItemWithoutReplayingCreation()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        await using var session = database.Open();
        var project = (await session.Create.Handle(new("PART", "Partial", ""), default)).Value;
        var services = Services(session);
        services.AddTransient<IPipelineBehavior<ChangeWorkItemStatusCommand, OperationResult<int>>, BusyStatus>();
        using var provider = services.BuildServiceProvider();
        var create = new CreateWorkItemCommand(project, WorkItemType.Task, "Keep created work", "",
            WorkItemPriority.Medium, null, null, null, null, []);

        // Act
        var saved = await provider.GetRequiredService<ISender>().Send(new CreateBoardWorkItemCommand(create, WorkItemStatus.Todo));

        // Assert
        Assert.Equal(OperationStatus.Success, saved.Status);
        Assert.False(saved.Value!.TargetReached);
        var work = await session.Context.WorkItems.SingleAsync();
        Assert.Equal(saved.Value.Id, work.Id);
        Assert.Equal(WorkItemStatus.Backlog, work.Status);
        Assert.Equal(1, work.Version);
        Assert.Single(await session.Context.ChangeEvents.Where(change => change.Action == "WorkCreated").ToListAsync());
    }

    private static IServiceCollection Services(ProjectSession session) => new ServiceCollection()
        .AddLogging()
        .AddMediatR(options => options.RegisterServicesFromAssemblyContaining<CreateBoardWorkItemCommand>())
        .AddSingleton<IWorkItemRepository>(new WorkItemRepository(session.Context))
        .AddSingleton<IProjectRepository>(session.Repository)
        .AddSingleton<ILabelRepository>(new LabelRepository(session.Context))
        .AddSingleton<IProjectAccess>(session.Access)
        .AddSingleton<IUnitOfWork>(session.Unit)
        .AddSingleton<ICurrentUser>(new TestUser("owner"))
        .AddSingleton(TimeProvider.System)
        .AddSingleton<WorkItemValidation>();
}

internal class BusyStatus : IPipelineBehavior<ChangeWorkItemStatusCommand, OperationResult<int>>
{
    public Task<OperationResult<int>> Handle(ChangeWorkItemStatusCommand request,
        RequestHandlerDelegate<OperationResult<int>> next, CancellationToken cancellationToken)
        => Task.FromResult(OperationResult<int>.Busy());
}
