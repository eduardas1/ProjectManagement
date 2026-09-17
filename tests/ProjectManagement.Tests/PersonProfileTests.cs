using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Application.Common;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Domain.Enums;
using ProjectManagement.Infrastructure.Repositories;

namespace ProjectManagement.Tests;

public class PersonProfileTests
{
    [Fact]
    public void PersonLabelsKeepUnicodeNamesFallbackAndStableColors()
    {
        // Arrange
        var person = new PersonSummary("member", "Émile", "Dupont", "emile@example.test");
        var renamed = person with { FirstName = "Another" };
        var legacy = new PersonSummary("old", "", "", "old@example.test");
        var namesake = new PersonSummary("another", "Émile", "Dupont", "another@example.test");

        // Act
        var label = person.DisplayName;

        // Assert
        Assert.Equal("Émile Dupont", label);
        Assert.Equal("É", person.Initial);
        Assert.Equal(person.Color, renamed.Color);
        Assert.Equal("Émile Dupont (emile@example.test)", person.ChoiceLabel);
        Assert.Equal("old@example.test", legacy.DisplayName);
        Assert.Equal("O", legacy.Initial);
        Assert.Equal(person.DisplayName, namesake.DisplayName);
        Assert.NotEqual(person.ChoiceLabel, namesake.ChoiceLabel);
    }

    [Fact]
    public async Task ProfileMigrationPreservesExistingUsersProjectsAndAssignments()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync("20260507161834_AddSavedFilters");
        int projectId;
        await using (var session = database.Open())
        {
            projectId = (await session.Create.Handle(new("OLD", "Existing project", "Existing notes"), default)).Value;
            session.Context.WorkItems.Add(Work(projectId, 1));
            await session.Context.SaveChangesAsync();
        }
        await using var context = database.OpenContext();
        var before = await context.Users.OrderBy(user => user.Id).Select(user => new { user.Id, user.Email, user.SecurityStamp }).ToListAsync();

        // Act
        await context.Database.MigrateAsync();
        var legacy = await new ProjectRepository(context).GetDetailsAsync(projectId, "owner", default);
        Assert.Equal(before.Single(user => user.Id == "owner").Email, legacy!.Owner.DisplayName);
        context.UserProfiles.Add(new UserProfile { UserId = "owner", FirstName = "John", LastName = "Doe" });
        await context.SaveChangesAsync();
        var details = await new ProjectRepository(context).GetDetailsAsync(projectId, "owner", default);

        // Assert
        Assert.Equal(before, await context.Users.OrderBy(user => user.Id).Select(user => new { user.Id, user.Email, user.SecurityStamp }).ToListAsync());
        Assert.Equal("John Doe", details!.Owner.DisplayName);
        Assert.Equal("owner", details.Owner.Id);
        Assert.Equal("Existing notes", details.Description);
        Assert.Equal("owner", (await context.ProjectMembers.SingleAsync()).UserId);
        var work = await context.WorkItems.SingleAsync();
        Assert.Equal("owner", work.AssigneeId);
        Assert.Equal("owner", work.CreatedById);
        Assert.Equal("Work 1", work.Title);
        context.UserProfiles.Add(new UserProfile { UserId = "missing", FirstName = "No", LastName = "Account" });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task BoardProfilesStayScopedAndDoNotQueryEachCard()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        int projectId;
        await using (var session = database.Open())
        {
            projectId = (await session.Create.Handle(new("PE", "People", ""), default)).Value;
            session.Context.UserProfiles.Add(new UserProfile { UserId = "owner", FirstName = "Alex", LastName = "Smith" });
            session.Context.WorkItems.Add(Work(projectId, 1));
            await session.Context.SaveChangesAsync();
        }
        var counter = new PersonReadCounter();
        await using var context = database.OpenContext(commandInterceptor: counter);
        var repository = new WorkItemReadRepository(context, new TestUser("owner"));

        // Act
        var first = await repository.GetBoardAsync(projectId, default);
        var singleCount = counter.Reads;
        context.WorkItems.AddRange(Enumerable.Range(2, 39).Select(number => Work(projectId, number)));
        await context.SaveChangesAsync();
        counter.Reads = 0;
        var many = await repository.GetBoardAsync(projectId, default);
        var manyCount = counter.Reads;
        var denied = await new WorkItemReadRepository(context, new TestUser("other")).GetBoardAsync(projectId, default);

        // Assert
        Assert.Equal(singleCount, manyCount);
        Assert.Equal(2, manyCount);
        Assert.Equal(40, many!.Columns.Sum(column => column.Total));
        Assert.All(many.Columns.SelectMany(column => column.Items), item => Assert.Equal("Alex Smith", item.Assignee!.DisplayName));
        Assert.Equal("owner", first!.Columns.SelectMany(column => column.Items).Single().Assignee!.Id);
        Assert.Null(denied);
    }

    private static WorkItem Work(int projectId, int number) => new()
    {
        ProjectId = projectId, Number = number, Title = "Work " + number, Type = WorkItemType.Task,
        Status = WorkItemStatus.Todo, Priority = WorkItemPriority.Medium, CreatedById = "owner",
        AssigneeId = "owner", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };
}

internal class PersonReadCounter : DbCommandInterceptor
{
    public int Reads { get; set; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Reads++;
        return ValueTask.FromResult(result);
    }
}
