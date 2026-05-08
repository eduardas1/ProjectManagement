using Microsoft.EntityFrameworkCore;
using Workboard.Application.Common;
using Workboard.Application.Projects;
using Workboard.Application.Search;
using Workboard.Domain.Entities;
using Workboard.Domain.Enums;
using Workboard.Infrastructure.Repositories;
using Workboard.Infrastructure.Search;

namespace Workboard.Tests;

public class WorkSearchRepositoryTests
{
    [Fact]
    public async Task BasicAndAdvancedFiltersReturnTheSameScopedRowsAndOptions()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var ids = await PrepareAsync(database);
        await using var session = database.Open();
        var search = Repository(session.Context, "owner");
        var basic = new WorkSearchInput { Text = "PAYMENT", Type = WorkItemType.Task, Status = WorkItemStatus.Todo,
            Priority = WorkItemPriority.High, AssigneeId = "member", Label = "billing",
            DeadlineFrom = new DateOnly(2026, 10, 1), DeadlineThrough = new DateOnly(2026, 11, 1) };

        // Act
        var a = await search.SearchAsync(ids.Project, basic, default);
        var b = await search.SearchAsync(ids.Project, new WorkSearchInput
        {
            Expression = "(title=*PAYMENT/i|description=*PAYMENT/i),type=Task,status=Todo,priority=High,assignee=member,label=billing,deadline>=2026-10-01,deadline<=2026-11-01"
        }, default);
        var isolation = await search.SearchAsync(ids.Project, new WorkSearchInput { Expression = "title=*Private|number=1" }, default);
        var foreign = await search.SearchAsync(ids.Foreign, new WorkSearchInput(), default);
        var options = await search.GetOptionsAsync(ids.Project, default);

        // Assert
        Assert.Equal(OperationStatus.Success, a.Status);
        Assert.Equal(OperationStatus.Success, b.Status);
        Assert.Equal(new[] { ids.First }, a.Value!.Items.Select(work => work.Id));
        Assert.Equal(a.Value.Items.Select(work => work.Id), b.Value!.Items.Select(work => work.Id));
        Assert.Equal(1, a.Value.Total);
        Assert.Equal(new[] { ids.First }, isolation.Value!.Items.Select(work => work.Id));
        Assert.Equal(OperationStatus.NotFound, foreign.Status);
        Assert.Null(await search.GetOptionsAsync(ids.Foreign, default));
        Assert.Equal(new[] { "billing", "operations" }, options!.Labels);
        Assert.DoesNotContain(options.Members, member => member.Id == "other");
    }

    [Fact]
    public async Task LiteralSyntaxAndUnicodeStayLiteralAndBadInputsAreNotEmptySuccesses()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var ids = await PrepareAsync(database);
        await using var session = database.Open();
        var values = new[] { "(one,two)|value/i", "^caret", "$cash", "=equals", "!=not", "<angle>", @"\path", "100%_value", "Ąžuolas" };
        foreach (var value in values) session.Context.WorkItems.Add(NewWork(ids.Project, 100 + Array.IndexOf(values, value), value));
        await session.Context.SaveChangesAsync();
        var search = Repository(session.Context, "owner");
        var matches = new List<OperationResult<WorkSearchPage>>();

        // Act
        foreach (var value in values) matches.Add(await search.SearchAsync(ids.Project, new WorkSearchInput { Text = value }, default));
        var invalid = new[]
        {
            new WorkSearchInput { Expression = "ProjectId=2" }, new WorkSearchInput { Expression = "title=*Ąžuolas/i" }, new WorkSearchInput { Expression = "label!=billing" },
            new WorkSearchInput { Ordering = "title junk desc" }, new WorkSearchInput { Page = 0 },
            new WorkSearchInput { PageSize = 101 }, new WorkSearchInput { Page = int.MaxValue, PageSize = 100 },
            new WorkSearchInput { Text = "ONE-999999999999" }, new WorkSearchInput { Status = (WorkItemStatus)999 },
            new WorkSearchInput { AssigneeId = "member", UnassignedOnly = true },
            new WorkSearchInput { DeadlineFrom = new DateOnly(2026, 11, 1), DeadlineThrough = new DateOnly(2026, 10, 1) }
        };
        var rejected = new List<OperationResult<WorkSearchPage>>();
        foreach (var input in invalid) rejected.Add(await search.SearchAsync(ids.Project, input, default));
        var empty = await search.SearchAsync(ids.Project, new WorkSearchInput { Text = "' OR 1=1 --" }, default);

        // Assert
        for (var index = 0; index < values.Length; index++)
        {
            Assert.Equal(OperationStatus.Success, matches[index].Status);
            Assert.True(matches[index].Value!.Total == 1, "Literal input " + index + " did not match exactly one row.");
            Assert.Equal(values[index], Assert.Single(matches[index].Value!.Items).Title);
        }
        Assert.All(rejected, result => Assert.Equal(OperationStatus.Invalid, result.Status));
        Assert.Equal(OperationStatus.Success, empty.Status);
        Assert.Equal(0, empty.Value!.Total);
    }

    [Fact]
    public async Task StablePagesArchiveNullsAndKeysUseTheCurrentQuery()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var ids = await PrepareAsync(database);
        await using var session = database.Open();
        var search = Repository(session.Context, "owner");
        var expected = await session.Context.WorkItems.AsNoTracking().Where(work => work.ProjectId == ids.Project && !work.IsArchived)
            .OrderBy(work => work.Title).ThenBy(work => work.Id).Select(work => work.Id).ToArrayAsync();
        var pages = new List<int>();

        // Act
        for (var page = 1; page <= 6; page++)
        {
            var result = await search.SearchAsync(ids.Project, new WorkSearchInput { Ordering = "title", PageSize = 5, Page = page }, default);
            Assert.Equal(OperationStatus.Success, result.Status);
            pages.AddRange(result.Value!.Items.Select(work => work.Id));
        }
        var last = await search.SearchAsync(ids.Project, new WorkSearchInput { Page = 999, PageSize = 5 }, default);
        var archived = await search.SearchAsync(ids.Project, new WorkSearchInput { Archived = true }, default);
        var missing = await search.SearchAsync(ids.Project, new WorkSearchInput { Expression = "assignee=,deadline=" }, default);
        var key = await search.SearchAsync(ids.Project, new WorkSearchInput { Text = "one-0001" }, default);
        var otherKey = await search.SearchAsync(ids.Project, new WorkSearchInput { Text = "TWO-1" }, default);

        // Assert
        Assert.Equal(expected, pages);
        Assert.Equal(expected.Length, pages.Distinct().Count());
        Assert.Equal(last.Value!.PageCount, last.Value.Page);
        Assert.Equal(new[] { ids.Archived }, archived.Value!.Items.Select(work => work.Id));
        Assert.Equal(25, missing.Value!.Total);
        Assert.Equal(new[] { ids.First }, key.Value!.Items.Select(work => work.Id));
        Assert.Equal(0, otherKey.Value!.Total);
    }

    [Fact]
    public async Task RevocationInvalidatesTheAlreadyUsedRepositoryAndArchivedProjectStaysReadable()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var ids = await PrepareAsync(database);
        await using var member = database.Open("member");
        var search = Repository(member.Context, "member");
        var before = await search.SearchAsync(ids.Project, new WorkSearchInput(), default);
        await using (var owner = database.Open())
        {
            var project = await owner.Context.Projects.SingleAsync(project => project.Id == ids.Project);
            project.IsArchived = true;
            await owner.Context.SaveChangesAsync();
        }
        var archivedProject = await search.SearchAsync(ids.Project, new WorkSearchInput(), default);

        // Act
        await using (var owner = database.Open())
        {
            var membership = await owner.Context.ProjectMembers.SingleAsync(row => row.ProjectId == ids.Project && row.UserId == "member");
            owner.Context.ProjectMembers.Remove(membership);
            await owner.Context.SaveChangesAsync();
        }
        var after = await search.SearchAsync(ids.Project, new WorkSearchInput { Expression = "number>0|number=1" }, default);
        var options = await search.GetOptionsAsync(ids.Project, default);

        // Assert
        Assert.Equal(OperationStatus.Success, before.Status);
        Assert.Equal(OperationStatus.Success, archivedProject.Status);
        Assert.True(archivedProject.Value!.Project.IsArchived);
        Assert.Equal(OperationStatus.NotFound, after.Status);
        Assert.Null(options);
    }

    private static WorkSearchRepository Repository(Workboard.Infrastructure.Data.ApplicationDbContext context, string actor)
        => new(context, new TestUser(actor), new WorkSearchSyntax(), new WorkItemSearchMapper());

    private static async Task<SearchIds> PrepareAsync(ProjectTestDatabase database)
    {
        await database.InitializeAsync();
        await using var owner = database.Open();
        await using var other = database.Open("other");
        var project = (await owner.Create.Handle(new CreateProjectCommand("ONE", "First", ""), default)).Value;
        var foreign = (await other.Create.Handle(new CreateProjectCommand("TWO", "Private project", ""), default)).Value;
        var context = owner.Context;
        context.ProjectMembers.Add(new ProjectMember { ProjectId = project, UserId = "member", JoinedAtUtc = DateTime.UtcNow });
        var first = NewWork(project, 1, "Payment flow"); first.Status = WorkItemStatus.Todo; first.Priority = WorkItemPriority.High;
        first.AssigneeId = "member"; first.DueDate = new DateOnly(2026, 10, 31);
        var archived = NewWork(project, 30, "Archived work"); archived.IsArchived = true;
        var privateWork = NewWork(foreign, 1, "Private payment"); privateWork.CreatedById = "other";
        context.WorkItems.AddRange(first, archived, privateWork);
        for (var number = 2; number <= 26; number++) context.WorkItems.Add(NewWork(project, number, "Same title"));
        var billing = new Label { ProjectId = project, Name = "billing", NormalizedName = "BILLING" };
        var operations = new Label { ProjectId = project, Name = "operations", NormalizedName = "OPERATIONS" };
        context.Labels.AddRange(billing, operations, new Label { ProjectId = foreign, Name = "private-label", NormalizedName = "PRIVATE-LABEL" });
        await context.SaveChangesAsync();
        context.WorkItemLabels.AddRange(new WorkItemLabel { ProjectId = project, WorkItemId = first.Id, LabelId = billing.Id },
            new WorkItemLabel { ProjectId = project, WorkItemId = first.Id, LabelId = operations.Id });
        await context.SaveChangesAsync();
        return new SearchIds(project, foreign, first.Id, archived.Id);
    }

    private static WorkItem NewWork(int projectId, int number, string title) => new()
    {
        ProjectId = projectId, Number = number, Title = title, Type = WorkItemType.Task,
        CreatedById = "owner", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };

    private record SearchIds(int Project, int Foreign, int First, int Archived);
}
