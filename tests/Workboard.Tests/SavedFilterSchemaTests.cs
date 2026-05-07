using Microsoft.EntityFrameworkCore;
using Workboard.Application.Projects;
using Workboard.Domain.Entities;

namespace Workboard.Tests;

public class SavedFilterSchemaTests
{
    [Fact]
    public async Task NamesAreUniquePerAccountAndProjectAndUtcVersionsPersist()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        int first;
        int second;
        await using (var session = database.Open())
        {
            first = (await session.Create.Handle(new CreateProjectCommand("ONE", "First", ""), default)).Value;
            second = (await session.Create.Handle(new CreateProjectCommand("TWO", "Second", ""), default)).Value;
        }

        // Act
        await using (var context = database.OpenContext())
        {
            context.SavedFilters.AddRange(NewFilter(first), NewFilter(first, "member"), NewFilter(second));
            await context.SaveChangesAsync();
        }
        await using var read = database.OpenContext();
        var filters = await read.SavedFilters.OrderBy(filter => filter.Id).ToListAsync();

        // Assert
        Assert.Equal(3, filters.Count);
        Assert.All(filters, filter =>
        {
            Assert.Equal(1, filter.Version);
            Assert.Equal("status=Todo", filter.Expression);
            Assert.Equal("number", filter.Ordering);
            Assert.Equal(DateTimeKind.Utc, filter.CreatedAtUtc.Kind);
            Assert.Equal(DateTimeKind.Utc, filter.UpdatedAtUtc.Kind);
        });
        Assert.Contains(await read.Database.GetAppliedMigrationsAsync(), name => name.EndsWith("_AddSavedFilters", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DuplicateInvalidAndOrphanDefinitionsDoNotEnterTheDatabase()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        int project;
        await using (var session = database.Open())
        {
            project = (await session.Create.Handle(new CreateProjectCommand("ONE", "First", ""), default)).Value;
            session.Context.SavedFilters.Add(NewFilter(project));
            await session.Context.SaveChangesAsync();
        }
        var invalid = new List<SavedFilter> { NewFilter(project), NewFilter(int.MaxValue), NewFilter(project, "missing-account") };
        var empty = NewFilter(project); empty.Name = " "; invalid.Add(empty);
        var name = NewFilter(project); name.Name = new string('a', 101); invalid.Add(name);
        var normalized = NewFilter(project); normalized.NormalizedName = ""; invalid.Add(normalized);
        var expression = NewFilter(project); expression.Expression = new string('a', 2001); invalid.Add(expression);
        var order = NewFilter(project); order.Ordering = new string('a', 201); invalid.Add(order);
        var version = NewFilter(project); version.Version = 0; invalid.Add(version);
        for (var index = 1; index < invalid.Count; index++)
        {
            if (invalid[index].NormalizedName.Length > 0) invalid[index].NormalizedName = "INVALID " + index;
            if (invalid[index].Name == "My work") invalid[index].Name = "Invalid " + index;
        }
        var failures = new List<Exception?>();

        // Act
        foreach (var filter in invalid)
        {
            await using var context = database.OpenContext();
            context.SavedFilters.Add(filter);
            failures.Add(await Record.ExceptionAsync(() => context.SaveChangesAsync()));
        }
        await using var read = database.OpenContext();

        // Assert
        Assert.All(failures, failure => Assert.IsType<DbUpdateException>(failure));
        Assert.Single(await read.SavedFilters.ToListAsync());
        Assert.Single(await read.ChangeEvents.ToListAsync());
    }

    private static SavedFilter NewFilter(int projectId, string ownerId = "owner") => new()
    {
        ProjectId = projectId, OwnerId = ownerId, Name = "My work", NormalizedName = "MY WORK",
        Expression = "status=Todo", Ordering = "number", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };
}
