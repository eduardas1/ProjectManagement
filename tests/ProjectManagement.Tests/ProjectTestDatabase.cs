using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Application.Projects;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Infrastructure.Repositories;

namespace ProjectManagement.Tests;

internal class ProjectTestDatabase : IAsyncDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "projectmanagement-tests-" + Guid.NewGuid());

    public async Task InitializeAsync(string? migration = null)
    {
        Directory.CreateDirectory(directory);
        await using var context = OpenContext();
        if (migration == null) await context.Database.MigrateAsync();
        else await context.GetService<IMigrator>().MigrateAsync(migration);
        foreach (var id in new[] { "owner", "member", "other" })
            context.Users.Add(new IdentityUser { Id = id, UserName = id + "@example.test", Email = id + "@example.test",
                NormalizedUserName = id.ToUpperInvariant() + "@EXAMPLE.TEST", NormalizedEmail = id.ToUpperInvariant() + "@EXAMPLE.TEST" });
        await context.SaveChangesAsync();
    }

    public ApplicationDbContext OpenContext(SaveChangesInterceptor? interceptor = null, DbCommandInterceptor? commandInterceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "tests.db"), ForeignKeys = true, Pooling = false, DefaultTimeout = 1
        }.ToString());
        if (interceptor != null) builder.AddInterceptors(interceptor);
        if (commandInterceptor != null) builder.AddInterceptors(commandInterceptor);
        return new ApplicationDbContext(builder.Options);
    }

    public ProjectSession Open(string userId = "owner", SaveChangesInterceptor? interceptor = null)
        => new(OpenContext(interceptor), new TestUser(userId));

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        Directory.Delete(directory, recursive: true);
    }
}

internal record TestUser(string UserId) : ICurrentUser;

internal class ProjectSession(ApplicationDbContext context, TestUser user) : IAsyncDisposable
{
    protected TestUser User => user;
    public ApplicationDbContext Context => context;
    public ProjectRepository Repository => new(context);
    public ProjectAccess Access => new(context, user);
    public UnitOfWork Unit => new(context);
    public CreateProjectHandler Create => new(Repository, Unit, user, TimeProvider.System);
    public UpdateProjectHandler Update => new(Repository, Access, Unit, user, TimeProvider.System);
    public AddProjectMemberHandler AddMember => new(Repository, Access, Unit, user, TimeProvider.System);
    public RemoveProjectMemberHandler RemoveMember => new(Repository, Access, Unit, user, TimeProvider.System, new WorkItemRepository(context));
    public TransferProjectOwnershipHandler Transfer => new(Repository, Access, Unit, user, TimeProvider.System);
    public ValueTask DisposeAsync() => context.DisposeAsync();
}

internal class RejectProjectEvent : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<ChangeEvent>().Any(entry => entry.State == EntityState.Added))
            throw new InvalidOperationException("Simulated event-write failure.");
        return new ValueTask<InterceptionResult<int>>(result);
    }
}
