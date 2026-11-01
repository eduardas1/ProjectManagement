using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Projects;
using ProjectManagement.Application.Reports;
using ProjectManagement.Application.Search;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Domain.Enums;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Infrastructure.Repositories;
using ProjectManagement.Infrastructure.Search;

namespace ProjectManagement.Tests;

public class ProjectProgressTests
{
    [Fact]
    public async Task CountsMatchIndependentLeafFixtureAndRefreshAfterStatusChange()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        await using var session = database.Open();
        var report = Repository(session.Context, "owner");

        // Act
        var result = await report.GetProgressAsync(project, new WorkSearchInput { PageSize = 1, Page = 5 }, default);
        var high = await report.GetProgressAsync(project, new WorkSearchInput { Priority = WorkItemPriority.High }, default);
        var low = await report.GetProgressAsync(project, new WorkSearchInput { Expression = "priority=Low" }, default);
        var done = await report.GetProgressAsync(project, new WorkSearchInput { Status = WorkItemStatus.Done }, default);
        var changed = await session.Context.WorkItems.SingleAsync(work => work.ProjectId == project && work.Number == 3);
        changed.Status = WorkItemStatus.Done;
        await session.Context.SaveChangesAsync();
        session.Context.ChangeTracker.Clear();
        var refreshed = await report.GetProgressAsync(project, new WorkSearchInput(), default);

        // Assert
        Assert.Equal(OperationStatus.Success, result.Status);
        var value = result.Value!;
        Assert.Equal(10, value.EligibleTotal);
        Assert.Equal(new[] { 1, 3, 1, 1, 1, 3 }, Enum.GetValues<WorkItemStatus>().Select(status => value.States[status]));
        Assert.Equal(new[] { 2, 4, 2, 2 }, Enum.GetValues<WorkItemPriority>().Select(priority => value.Priorities[priority]));
        Assert.Equal(7, value.CompletionTotal);
        Assert.Equal(14.3m, value.CompletionPercent);
        Assert.Equal(2, value.Overdue);
        Assert.Equal(new DateOnly(2026, 10, 22), value.ReportDate);
        Assert.Equal(2, high.Value!.EligibleTotal);
        Assert.Equal(1, high.Value.Overdue);
        Assert.Equal(0m, high.Value.CompletionPercent);
        Assert.Equal(2, low.Value!.EligibleTotal);
        Assert.Equal(1, low.Value.CompletionTotal);
        Assert.Equal(0, low.Value.Overdue);
        Assert.Equal(1, done.Value!.EligibleTotal);
        Assert.Equal(100m, done.Value.CompletionPercent);
        Assert.Equal(0, done.Value.Overdue);
        Assert.Equal(10, refreshed.Value!.EligibleTotal);
        Assert.Equal(28.6m, refreshed.Value.CompletionPercent);
        Assert.Equal(1, refreshed.Value.Overdue);
    }

    [Fact]
    public async Task EmptyCancelledAndInvalidFiltersStayDistinct()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        await using var session = database.Open();
        var report = Repository(session.Context, "owner");

        // Act
        var empty = await report.GetProgressAsync(project, new WorkSearchInput { Expression = "number=999" }, default);
        var cancelled = await report.GetProgressAsync(project, new WorkSearchInput { Expression = "status=Cancelled" }, default);
        var errors = new List<OperationResult<ProjectProgress>>();
        foreach (var input in new[] { new WorkSearchInput { Expression = "ProjectId=2" },
            new WorkSearchInput { Expression = "status=(" },
            new WorkSearchInput { Ordering = "title junk desc" }, new WorkSearchInput { Page = 0 },
            new WorkSearchInput { DeadlineFrom = new DateOnly(2026, 11, 1), DeadlineThrough = new DateOnly(2026, 10, 1) } })
            errors.Add(await report.GetProgressAsync(project, input, default));

        // Assert
        Assert.Equal(OperationStatus.Success, empty.Status);
        Assert.Equal(0, empty.Value!.EligibleTotal);
        Assert.Null(empty.Value.CompletionPercent);
        Assert.All(empty.Value.States.Values, count => Assert.Equal(0, count));
        Assert.All(empty.Value.Priorities.Values, count => Assert.Equal(0, count));
        Assert.Equal(3, cancelled.Value!.EligibleTotal);
        Assert.Equal(3, cancelled.Value.Cancelled);
        Assert.Equal(0, cancelled.Value.CompletionTotal);
        Assert.Null(cancelled.Value.CompletionPercent);
        Assert.Equal(0, cancelled.Value.Overdue);
        Assert.All(errors, error => Assert.Equal(OperationStatus.Invalid, error.Status));
    }

    [Fact]
    public async Task CurrentMembershipRejectsRetiredProjectReads()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        await using var member = database.Open("member");
        var report = Repository(member.Context, "member");
        var before = await report.GetProgressAsync(project, new WorkSearchInput { Expression = "number=12|number>0" }, default);
        var foreign = await report.GetProgressAsync(project + 1, new WorkSearchInput(), default);
        await using (var owner = database.Open())
        {
            var row = await owner.Context.Projects.SingleAsync(row => row.Id == project);
            row.IsArchived = true;
            await owner.Context.SaveChangesAsync();
        }
        var archived = await report.GetProgressAsync(project, new WorkSearchInput(), default);

        // Act
        await using (var owner = database.Open())
        {
            var membership = await owner.Context.ProjectMembers.SingleAsync(row => row.ProjectId == project && row.UserId == "member");
            owner.Context.ProjectMembers.Remove(membership);
            await owner.Context.SaveChangesAsync();
        }
        var revoked = await report.GetProgressAsync(project, new WorkSearchInput { Expression = "number>0|number=12" }, default);

        // Assert
        Assert.Equal(10, before.Value!.EligibleTotal);
        Assert.Equal(OperationStatus.NotFound, foreign.Status);
        Assert.Equal(OperationStatus.NotFound, archived.Status);
        Assert.Null(archived.Value);
        Assert.Equal(OperationStatus.NotFound, revoked.Status);
    }

    [Theory]
    [InlineData("2026-10-21T21:30:00Z", "Europe/Vilnius", 21, 22)]
    [InlineData("2026-10-21T21:30:00Z", "UTC", 21, 21)]
    [InlineData("2026-10-25T00:30:00Z", "Europe/Vilnius", 25, 25)]
    [InlineData("2026-10-25T01:30:00Z", "Europe/Vilnius", 25, 25)]
    public void ReportingCalendarUsesConfiguredDateAcrossMidnightAndClockChange(string instant, string zone, int utcDay, int localDay)
    {
        // Arrange
        var clock = new FixedReportTime(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture));
        var calendar = new ReportingCalendar(clock, TimeZoneInfo.FindSystemTimeZoneById(zone));

        // Act
        var date = calendar.Today;

        // Assert
        Assert.Equal(utcDay, clock.GetUtcNow().Day);
        Assert.Equal(new DateOnly(2026, 10, localDay), date);
        Assert.Equal(zone, calendar.TimeZoneId);
    }

    private static ProjectProgressRepository Repository(ApplicationDbContext context, string user)
        => new(context, new TestUser(user), new WorkSearchSyntax(), new WorkItemSearchMapper(),
            new ReportingCalendar(new FixedReportTime(new DateTimeOffset(2026, 10, 21, 21, 30, 0, TimeSpan.Zero)),
                TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius")));

    private static async Task<int> PrepareAsync(ProjectTestDatabase database)
    {
        await database.InitializeAsync();
        await using var owner = database.Open();
        await using var other = database.Open("other");
        var project = (await owner.Create.Handle(new CreateProjectCommand("REPORT", "Progress fixture", ""), default)).Value;
        var foreign = (await other.Create.Handle(new CreateProjectCommand("OTHER", "Private work", ""), default)).Value;
        owner.Context.ProjectMembers.Add(new ProjectMember { ProjectId = project, UserId = "member", JoinedAtUtc = DateTime.UtcNow });
        var rows = new (WorkItemType Type, WorkItemStatus Status, WorkItemPriority Priority, int? Parent, int? Epic, bool Archived, int? DueDay)[]
        {
            (WorkItemType.Epic, WorkItemStatus.InProgress, WorkItemPriority.Low, null, null, false, 21),
            (WorkItemType.Story, WorkItemStatus.Done, WorkItemPriority.High, null, 1, false, 21),
            (WorkItemType.Subtask, WorkItemStatus.Todo, WorkItemPriority.High, 2, null, false, 21),
            (WorkItemType.Subtask, WorkItemStatus.Cancelled, WorkItemPriority.Low, 2, null, false, 21),
            (WorkItemType.Task, WorkItemStatus.Done, WorkItemPriority.Medium, null, null, false, 21),
            (WorkItemType.Bug, WorkItemStatus.InReview, WorkItemPriority.Critical, null, null, false, 22),
            (WorkItemType.Task, WorkItemStatus.Backlog, WorkItemPriority.Low, null, null, false, null),
            (WorkItemType.Story, WorkItemStatus.Todo, WorkItemPriority.Medium, null, null, false, 21),
            (WorkItemType.Subtask, WorkItemStatus.Done, WorkItemPriority.Critical, 8, null, true, 21),
            (WorkItemType.Task, WorkItemStatus.Cancelled, WorkItemPriority.Critical, null, null, false, 21),
            (WorkItemType.Story, WorkItemStatus.InProgress, WorkItemPriority.High, null, 1, false, 23),
            (WorkItemType.Bug, WorkItemStatus.Todo, WorkItemPriority.High, null, null, false, 21),
            (WorkItemType.Task, WorkItemStatus.Done, WorkItemPriority.Low, null, null, true, 21),
            (WorkItemType.Task, WorkItemStatus.Todo, WorkItemPriority.Medium, null, null, false, 23),
            (WorkItemType.Story, WorkItemStatus.Todo, WorkItemPriority.Low, null, null, false, null),
            (WorkItemType.Subtask, WorkItemStatus.Cancelled, WorkItemPriority.Medium, 15, null, false, 21)
        };
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            owner.Context.WorkItems.Add(new WorkItem { Id = index + 1, ProjectId = index == 11 ? foreign : project,
                Number = index + 1, Title = "Fixture " + (index + 1), Type = row.Type, Status = row.Status, Priority = row.Priority,
                ParentId = row.Parent, EpicId = row.Epic, IsArchived = row.Archived,
                DueDate = row.DueDay.HasValue ? new DateOnly(2026, 10, row.DueDay.Value) : null,
                CreatedById = index == 11 ? "other" : "owner", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            await owner.Context.SaveChangesAsync();
        }
        return project;
    }
}

internal class FixedReportTime(DateTimeOffset instant) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => instant;
}
