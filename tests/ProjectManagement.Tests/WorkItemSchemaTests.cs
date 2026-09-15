using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Domain.Enums;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Tests;

public class WorkItemSchemaTests
{
    [Fact]
    public async Task ProjectOnlyDatabaseUpgradesWithoutChangingExistingRecords()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260107192223_AddProjects");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO AspNetUsers (Id,UserName,Email,EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount)
                VALUES ('original','original@example.test','original@example.test',0,0,0,0,0);
            INSERT INTO Projects (Id,Key,Name,Description,OwnerId,NextWorkItemNumber,IsArchived,Version,CreatedAtUtc,UpdatedAtUtc)
                VALUES (1,'OLD','Original','Retained','original',1,0,3,'2026-10-21 12:00:00','2026-10-21 12:00:00');
            INSERT INTO ProjectMembers (ProjectId,UserId,JoinedAtUtc) VALUES (1,'original','2026-10-21 12:00:00');
            INSERT INTO ChangeEvents (ProjectId,ActorId,OccurredAtUtc,Action,Summary,DetailsJson)
                VALUES (1,'original','2026-10-21 12:00:00','ProjectCreated','Original event','{{}}');
            """);

        // Act
        await migrator.MigrateAsync();
        context.WorkItems.Add(new WorkItem { ProjectId = 1, Number = 1, Type = WorkItemType.Task,
            Title = "First work", CreatedById = "original", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        // Assert
        var project = await context.Projects.SingleAsync();
        Assert.Equal("Retained", project.Description);
        Assert.Equal(3, project.Version);
        Assert.Equal("original", (await context.Users.SingleAsync()).Id);
        Assert.Equal("Original event", (await context.ChangeEvents.SingleAsync()).Summary);
        Assert.Null((await context.ChangeEvents.SingleAsync()).WorkItemId);
        Assert.Single(await context.WorkItems.ToListAsync());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(), migration => migration.EndsWith("_AddWorkItems", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SameProjectReferencesAndSingleDuplicateTargetAreDatabaseConstraints()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        int first, second;
        await using (var session = database.Open())
        {
            first = (await session.Create.Handle(new("AB", "First", ""), default)).Value;
            second = (await session.Create.Handle(new("CD", "Second", ""), default)).Value;
        }
        int sourceId, targetId, nextId, otherId;
        await using (var context = database.OpenContext())
        {
            var source = NewWork(first, 1);
            var target = NewWork(first, 2);
            var next = NewWork(first, 3);
            var other = NewWork(second, 1);
            context.WorkItems.AddRange(source, target, next, other);
            await context.SaveChangesAsync();
            sourceId = source.Id; targetId = target.Id; nextId = next.Id; otherId = other.Id;
            context.WorkItemLinks.Add(new WorkItemLink { ProjectId = first, SourceId = sourceId,
                TargetId = targetId, Kind = WorkItemLinkKind.Duplicates, CreatedById = "owner", CreatedAtUtc = DateTime.UtcNow });
            await context.SaveChangesAsync();
        }

        // Act
        await using var foreign = database.OpenContext();
        foreign.WorkItemLinks.Add(new WorkItemLink { ProjectId = first, SourceId = targetId,
            TargetId = otherId, Kind = WorkItemLinkKind.Blocks, CreatedById = "owner", CreatedAtUtc = DateTime.UtcNow });
        var foreignFailure = await Record.ExceptionAsync(() => foreign.SaveChangesAsync());
        await using var duplicate = database.OpenContext();
        duplicate.WorkItemLinks.Add(new WorkItemLink { ProjectId = first, SourceId = sourceId,
            TargetId = nextId, Kind = WorkItemLinkKind.Duplicates, CreatedById = "owner", CreatedAtUtc = DateTime.UtcNow });
        var duplicateFailure = await Record.ExceptionAsync(() => duplicate.SaveChangesAsync());

        // Assert
        Assert.IsType<DbUpdateException>(foreignFailure);
        Assert.IsType<DbUpdateException>(duplicateFailure);
        await using var read = database.OpenContext();
        Assert.Single(await read.WorkItemLinks.ToListAsync());
        Assert.Equal(4, await read.WorkItems.CountAsync());
    }

    private static WorkItem NewWork(int projectId, int number) => new()
    {
        ProjectId = projectId, Number = number, Type = WorkItemType.Task, Title = "Task",
        CreatedById = "owner", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };
}
