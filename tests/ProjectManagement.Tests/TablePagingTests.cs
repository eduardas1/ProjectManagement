using ProjectManagement.Application.Common;
using ProjectManagement.Application.Projects;
using ProjectManagement.Application.Search;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Domain.Enums;
using ProjectManagement.Infrastructure.Repositories;
using ProjectManagement.Infrastructure.Search;

namespace ProjectManagement.Tests;

public class TablePagingTests
{
    [Fact]
    public async Task WorkListsAndSearchSortTheWholeSelectionBeforePaging()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        await using var session = database.Open();
        var project = (await session.Create.Handle(new CreateProjectCommand("PAGE", "Paging", ""), default)).Value;
        var foreign = (await session.Create.Handle(new CreateProjectCommand("OTHER", "Other", ""), default)).Value;
        for (var number = 1; number <= 35; number++)
            session.Context.WorkItems.Add(new WorkItem { ProjectId = project, Number = number, Title = $"Item {36 - number:00}",
                Type = WorkItemType.Task, Status = WorkItemStatus.Todo, CreatedById = "owner", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        session.Context.WorkItems.Add(new WorkItem { ProjectId = foreign, Number = 1, Title = "AAA foreign", CreatedById = "owner" });
        await session.Context.SaveChangesAsync();
        var reader = new WorkItemReadRepository(session.Context, new TestUser("owner"));
        var search = new WorkSearchRepository(session.Context, new TestUser("owner"), new WorkSearchSyntax(), new WorkItemSearchMapper());

        // Act
        var page = await reader.ListAsync(project, 2, default, table: new("title", false, 10));
        var descending = await reader.ListAsync(project, 1, default, table: new("title", true, 10));
        var invalid = await reader.ListAsync(project, int.MaxValue, default, table: new("ProjectId", true, int.MaxValue));
        var found = await search.SearchAsync(project, new WorkSearchInput { Ordering = "title", Page = 2, PageSize = 10, Expression = "status=Todo" }, default);
        var denied = await new WorkItemReadRepository(session.Context, new TestUser("other")).ListAsync(project, 1, default);
        var assignee = await search.SearchAsync(project, new WorkSearchInput { Ordering = "assignee desc" }, default);

        // Assert
        Assert.Equal(35, page!.Total);
        Assert.Equal(4, page.PageCount);
        Assert.Equal(Enumerable.Range(11, 10).Select(n => $"Item {n:00}"), page.Items.Select(item => item.Title));
        Assert.Equal("Item 35", descending!.Items[0].Title);
        Assert.Equal("number", invalid!.Table!.Sort);
        Assert.Equal(20, invalid.Table.PageSize);
        Assert.Equal(2, invalid.Page);
        Assert.Equal(page.Items.Select(item => item.Id), found.Value!.Items.Select(item => item.Id));
        Assert.Null(denied);
        Assert.NotNull(assignee.Value);
        Assert.NotNull(new WorkSearchSyntax().ValidateOrdering("ProjectId"));
    }

    [Fact]
    public async Task DocumentListsUseTheSameBoundedPagingAndSortContract()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        await using var session = database.Open();
        var project = (await session.Create.Handle(new CreateProjectCommand("DOCS", "Documents", ""), default)).Value;
        for (var number = 1; number <= 24; number++)
            session.Context.ProjectDocuments.Add(new ProjectDocument { ProjectId = project, Title = $"Note {25 - number:00}",
                Body = "", CreatedById = "owner", UpdatedById = "owner", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await session.Context.SaveChangesAsync();
        var reader = new CollaborationReadRepository(session.Context, new TestUser("owner"));

        // Act
        var page = await reader.ListDocumentsAsync(project, 2, default, new("title", false, 10));
        var newest = await reader.ListDocumentsAsync(project, 1, default, new("title", true, 10));
        var invalid = await reader.ListDocumentsAsync(project, -1, default, new("Body", false, 0));

        // Assert
        Assert.Equal(24, page!.Total);
        Assert.Equal("Note 11", page.Items[0].Title);
        Assert.Equal("Note 24", newest!.Items[0].Title);
        Assert.Equal(1, invalid!.Page);
        Assert.Equal("title", invalid.Table!.Sort);
        Assert.Equal(20, invalid.Items.Count);
    }
}
