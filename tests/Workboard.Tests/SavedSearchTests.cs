using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workboard.Application.Common;
using Workboard.Application.Projects;
using Workboard.Application.Search;
using Workboard.Domain.Entities;
using Workboard.Infrastructure.Data;
using Workboard.Infrastructure.Repositories;
using Workboard.Infrastructure.Search;

namespace Workboard.Tests;

public class SavedSearchTests
{
    [Fact]
    public async Task CreateRenameNoOpAndDeletePersistOnlyOwnPrivateDefinition()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        await using var session = database.Open();
        var own = new SavedSearchSession(session.Context, "owner");
        var events = await session.Context.ChangeEvents.CountAsync();

        // Act
        var created = await own.Create.Handle(new(project, "  My tasks  ", "status=Todo", "title desc"), default);
        var id = created.Value;
        var first = await own.Repository.GetAsync(project, id, default);
        var renamed = await own.Rename.Handle(new(project, id, 1, "My pending tasks"), default);
        var changed = await own.Repository.GetAsync(project, id, default);
        var unchanged = await own.Rename.Handle(new(project, id, 2, "My pending tasks"), default);
        var noOp = await own.Repository.GetAsync(project, id, default);
        var duplicate = await own.Create.Handle(new(project, "my PENDING tasks", "", ""), default);
        var member = new SavedSearchSession(session.Context, "member");
        var sameName = await member.Create.Handle(new(project, "My pending tasks", "", ""), default);
        var memberList = await member.Repository.ListAsync(project, default);
        var removed = await own.Delete.Handle(new(project, id, 2), default);

        // Assert
        Assert.Equal(OperationStatus.Success, created.Status);
        Assert.Equal("My tasks", first!.Name);
        Assert.Equal("status=Todo", first.Expression);
        Assert.Equal("title desc", first.Ordering);
        Assert.Equal(OperationStatus.Success, renamed.Status);
        Assert.Equal(2, changed!.Version);
        Assert.Equal(OperationStatus.Success, unchanged.Status);
        Assert.Equal(changed, noOp);
        Assert.Equal(OperationStatus.Invalid, duplicate.Status);
        Assert.Equal(OperationStatus.Success, sameName.Status);
        Assert.Equal(sameName.Value, Assert.Single(memberList!.Items).Id);
        Assert.Equal(OperationStatus.Success, removed.Status);
        Assert.Null(await own.Repository.GetAsync(project, id, default));
        Assert.Equal(events, await session.Context.ChangeEvents.CountAsync());
        var saved = await session.Context.SavedFilters.AsNoTracking().SingleAsync();
        Assert.Equal("member", saved.OwnerId);
        Assert.Equal(DateTimeKind.Utc, saved.CreatedAtUtc.Kind);
        Assert.Equal(saved.CreatedAtUtc, saved.UpdatedAtUtc);
    }

    [Fact]
    public async Task OwnershipRevocationAndArchiveAreCheckedForEveryOperation()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        await using var session = database.Open("member");
        var member = new SavedSearchSession(session.Context, "member");
        var id = (await member.Create.Handle(new(project, "Private", "", ""), default)).Value;
        await using var ownerSession = database.Open();
        var owner = new SavedSearchSession(ownerSession.Context, "owner");

        // Act
        // Assert
        Assert.Null(await owner.Repository.GetAsync(project, id, default));
        Assert.Equal(OperationStatus.NotFound, (await owner.Rename.Handle(new(project, id, 1, "Stolen"), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await owner.Delete.Handle(new(project, id, 1), default)).Status);
        Assert.Equal(OperationStatus.Success, (await ownerSession.Archive.Handle(new(project, 2, true), default)).Status);
        Assert.NotNull(await member.Repository.GetAsync(project, id, default));
        session.Context.ChangeTracker.Clear();
        Assert.Equal(OperationStatus.Invalid, (await member.Create.Handle(new(project, "Blocked", "", ""), default)).Status);
        Assert.Equal(OperationStatus.Invalid, (await member.Rename.Handle(new(project, id, 1, "Blocked"), default)).Status);
        Assert.Equal(OperationStatus.Invalid, (await member.Delete.Handle(new(project, id, 1), default)).Status);
        Assert.Equal(OperationStatus.Success, (await ownerSession.Archive.Handle(new(project, 3, false), default)).Status);
        session.Context.ChangeTracker.Clear();
        Assert.Equal(OperationStatus.Success, (await ownerSession.RemoveMember.Handle(new(project, 4, "member"), default)).Status);
        Assert.Null(await member.Repository.ListAsync(project, default));
        Assert.Null(await member.Repository.GetAsync(project, id, default));
        Assert.Equal(OperationStatus.NotFound, (await member.Create.Handle(new(project, "Blocked", "", ""), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await member.Rename.Handle(new(project, id, 1, "Blocked"), default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await member.Delete.Handle(new(project, id, 1), default)).Status);
        Assert.Equal(OperationStatus.Success, (await ownerSession.AddMember.Handle(new(project, 5, "member@example.test"), default)).Status);
        Assert.NotNull(await member.Repository.GetAsync(project, id, default));
    }

    [Fact]
    public async Task StaleNoOpDuplicateInvalidAndOverflowDoNotChangeStoredDefinition()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        await using var session = database.Open();
        var own = new SavedSearchSession(session.Context, "owner");
        var id = (await own.Create.Handle(new(project, "First", "", ""), default)).Value;
        await own.Create.Handle(new(project, "Second", "", ""), default);

        // Act
        await own.Rename.Handle(new(project, id, 1, "Updated"), default);
        var staleRename = await own.Rename.Handle(new(project, id, 1, "Updated"), default);
        var staleDelete = await own.Delete.Handle(new(project, id, 1), default);
        var duplicate = await own.Rename.Handle(new(project, id, 2, "second"), default);
        var invalid = await own.Create.Handle(new(project, "Broken", "ProjectId=1", ""), default);
        var badOrder = await own.Create.Handle(new(project, "Broken", "", "title junk desc"), default);
        var archived = await own.Create.Handle(new(project, "Broken", "", "", true), default);
        var row = await session.Context.SavedFilters.SingleAsync(filter => filter.Id == id);
        row.Version = long.MaxValue;
        await session.Context.SaveChangesAsync();
        var overflow = await own.Rename.Handle(new(project, id, long.MaxValue, "Changed"), default);

        // Assert
        Assert.Equal(OperationStatus.Conflict, staleRename.Status);
        Assert.Equal(OperationStatus.Conflict, staleDelete.Status);
        Assert.Equal(OperationStatus.Invalid, duplicate.Status);
        Assert.Equal(OperationStatus.Invalid, invalid.Status);
        Assert.Equal(OperationStatus.Invalid, badOrder.Status);
        Assert.Equal(OperationStatus.Invalid, archived.Status);
        Assert.Equal(OperationStatus.Invalid, overflow.Status);
        Assert.Equal("Updated", (await own.Repository.GetAsync(project, id, default))!.Name);
        Assert.Equal(2, await session.Context.SavedFilters.CountAsync());
    }

    [Fact]
    public async Task BusyAndFailedSaveRollBackWithoutCreatingPrivateHistory()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        await using var blocker = database.Open();
        await using var contender = database.Open();
        var own = new SavedSearchSession(contender.Context, "owner");
        OperationResult<int> busy;

        // Act
        await using (var transaction = await blocker.Unit.BeginWriteAsync(default))
            busy = await own.Create.Handle(new(project, "Busy", "", ""), default);
        await using (var rejected = database.Open(interceptor: new RejectSavedSearch()))
        {
            var fail = new SavedSearchSession(rejected.Context, "owner");
            await Assert.ThrowsAsync<InvalidOperationException>(() => fail.Create.Handle(new(project, "Rejected", "", ""), default));
        }
        var retry = await own.Create.Handle(new(project, "Busy", "", ""), default);

        // Assert
        Assert.Equal(OperationStatus.Busy, busy.Status);
        Assert.Equal(OperationStatus.Success, retry.Status);
        await using var verify = database.Open();
        Assert.Equal("Busy", (await verify.Context.SavedFilters.SingleAsync()).Name);
        Assert.False(await verify.Context.ChangeEvents.AnyAsync(change => change.Summary.Contains("Busy") || change.Summary.Contains("Rejected")));
    }

    [Fact]
    public async Task StoredInvalidSyntaxIsRetainedAndRejectedByCurrentSearchRules()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        var project = await PrepareAsync(database);
        await using var session = database.Open();
        var own = new SavedSearchSession(session.Context, "owner");
        var id = (await own.Create.Handle(new(project, "Retained", "status=Todo", "number"), default)).Value;
        var stored = await session.Context.SavedFilters.SingleAsync(filter => filter.Id == id);
        stored.Expression = "ProjectId=99";
        await session.Context.SaveChangesAsync();
        var filter = await own.Repository.GetAsync(project, id, default);
        var search = new WorkSearchRepository(session.Context, new TestUser("owner"), new WorkSearchSyntax(), new WorkItemSearchMapper());

        // Act
        var result = await search.SearchAsync(project, new WorkSearchInput { Expression = filter!.Expression, Ordering = filter.Ordering }, default);

        // Assert
        Assert.Equal(OperationStatus.Invalid, result.Status);
        Assert.Equal("Expression", result.Field);
        Assert.Equal("ProjectId=99", (await own.Repository.GetAsync(project, id, default))!.Expression);
        Assert.Equal(1, (await own.Repository.GetAsync(project, id, default))!.Version);
    }

    private static async Task<int> PrepareAsync(ProjectTestDatabase database)
    {
        await database.InitializeAsync();
        await using var owner = database.Open();
        var id = (await owner.Create.Handle(new CreateProjectCommand("TEST", "Search project", ""), default)).Value;
        await owner.AddMember.Handle(new AddProjectMemberCommand(id, 1, "member@example.test"), default);
        return id;
    }
}

internal class SavedSearchSession(ApplicationDbContext context, string actor)
{
    private TestUser User => new(actor);
    public SavedSearchRepository Repository => new(context, User);
    private ProjectAccess Access => new(context, User);
    private UnitOfWork Unit => new(context);
    public CreateSavedSearchHandler Create => new(Repository, Access, Unit, User, new WorkSearchSyntax(), TimeProvider.System);
    public RenameSavedSearchHandler Rename => new(Repository, Access, Unit, TimeProvider.System);
    public DeleteSavedSearchHandler Delete => new(Repository, Access, Unit);
}

internal class RejectSavedSearch : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<SavedFilter>().Any(entry => entry.State == EntityState.Added))
            throw new InvalidOperationException("Simulated saved search failure.");
        return new ValueTask<InterceptionResult<int>>(result);
    }
}
