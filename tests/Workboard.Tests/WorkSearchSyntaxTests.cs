using Gridify;
using Microsoft.EntityFrameworkCore;
using Workboard.Application.Projects;
using Workboard.Domain.Entities;
using Workboard.Domain.Enums;
using Workboard.Infrastructure.Search;

namespace Workboard.Tests;

public class WorkSearchSyntaxTests
{
    [Fact]
    public void SupportedDefinitionsKeepTheirMeaningAndMalformedOrComplexOnesAreRejected()
    {
        // Arrange
        var syntax = new WorkSearchSyntax();
        var accepted = new[] { "", "(status=Todo|priority=High),number>=0", @"title=\(one\),number=2", "deadline=", "assignee!=", "label=*billing/i",
            string.Join(",", Enumerable.Repeat("number>0", 32)), new string('(', 8) + "number=1" + new string(')', 8) };
        var rejected = new[] { "ProjectId=2", "title[0]=x", "status=123", "status=todo", "priority>Low", "deadline=31/10/2026", "deadline=2026-02-30",
            "deadline=/i", "label!=billing", "label=", "number=9999999999999", "number=*1", "assignee^a", "number=1=2", "status=(",
            new string('(', 9) + "number=1" + new string(')', 9), string.Join(",", Enumerable.Repeat("number>0", 33)), @"title=hello\", new string('x', 2001) };

        // Act
        var valid = accepted.Select(syntax.ValidateExpression).ToArray();
        var errors = rejected.Select(syntax.ValidateExpression).ToArray();

        // Assert
        Assert.All(valid, Assert.Null);
        Assert.All(errors, error => Assert.False(string.IsNullOrEmpty(error)));
        Assert.Null(syntax.ValidateOrdering("title desc,deadline asc,number"));
        Assert.All(new[] { "title junk desc", "title?,number", "label", "title,title", "title,", "ProjectId", "number sideways" },
            order => Assert.NotNull(syntax.ValidateOrdering(order)));
    }

    [Fact]
    public async Task MappedGroupsLabelsNullsAndLiteralStringsTranslateInsideTheProjectScope()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        await using var session = database.Open();
        var project = (await session.Create.Handle(new CreateProjectCommand("ONE", "First", ""), default)).Value;
        var foreign = (await session.Create.Handle(new CreateProjectCommand("TWO", "Other", ""), default)).Value;
        var context = session.Context;
        var first = NewWork(project, 1, "Payment (one,two)|value/i", WorkItemStatus.Todo);
        first.DueDate = new DateOnly(2026, 10, 31);
        var second = NewWork(project, 2, "Login", WorkItemStatus.Done);
        context.WorkItems.AddRange(first, second, NewWork(foreign, 1, "Private payment", WorkItemStatus.Todo));
        var billing = new Label { ProjectId = project, Name = "billing", NormalizedName = "BILLING" };
        var operations = new Label { ProjectId = project, Name = "operations", NormalizedName = "OPERATIONS" };
        context.Labels.AddRange(billing, operations);
        await context.SaveChangesAsync();
        context.WorkItemLabels.AddRange(new WorkItemLabel { ProjectId = project, WorkItemId = first.Id, LabelId = billing.Id },
            new WorkItemLabel { ProjectId = project, WorkItemId = first.Id, LabelId = operations.Id });
        await context.SaveChangesAsync();
        var mapper = new WorkItemSearchMapper();
        var scoped = context.WorkItems.AsNoTracking().Where(work => work.ProjectId == project);

        // Act
        var grouped = await scoped.ApplyFiltering("(status=Todo|status=Done),number>0", mapper).OrderBy(work => work.Number).Select(work => work.Number).ToArrayAsync();
        var isolated = await scoped.ApplyFiltering("title=*Private|status=Todo", mapper).Select(work => work.Id).ToArrayAsync();
        var labels = await scoped.ApplyFiltering("label=*ILL/i", mapper).Select(work => work.Id).ToArrayAsync();
        var missingDate = await scoped.ApplyFiltering("deadline=", mapper).Select(work => work.Id).ToArrayAsync();
        var literal = await scoped.ApplyFiltering(@"title=Payment \(one\,two\)\|value\/i", mapper).Select(work => work.Id).ToArrayAsync();
        var ordered = await ((IOrderedQueryable<WorkItem>)scoped.ApplyOrdering("title desc,number", mapper)).ThenBy(work => work.Id).Select(work => work.Number).ToArrayAsync();

        // Assert
        Assert.Equal(new[] { 1, 2 }, grouped);
        Assert.Equal(new[] { first.Id }, isolated);
        Assert.Equal(new[] { first.Id }, labels);
        Assert.Equal(new[] { second.Id }, missingDate);
        Assert.Equal(new[] { first.Id }, literal);
        Assert.Equal(new[] { 1, 2 }, ordered);
        Assert.NotNull(new WorkSearchSyntax().ValidateExpression("label!=billing"));
    }

    private static WorkItem NewWork(int projectId, int number, string title, WorkItemStatus status) => new()
    {
        ProjectId = projectId, Number = number, Title = title, Status = status, Type = WorkItemType.Task,
        CreatedById = "owner", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };
}
