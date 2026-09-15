using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Projects;

namespace ProjectManagement.Tests;

public class ProjectCommandTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("1TEAM")]
    [InlineData("TEAM-1")]
    [InlineData("A1234567890")]
    [InlineData("AB\nCD")]
    public async Task CreateRejectsInvalidKeys(string key)
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        await using var session = database.Open();

        // Act
        var result = await session.Create.Handle(new(key, "Project", ""), default);

        // Assert
        Assert.Equal(OperationStatus.Invalid, result.Status);
        Assert.False(await session.Context.Projects.AnyAsync());
        Assert.False(await session.Context.ChangeEvents.AnyAsync());
    }

    [Fact]
    public async Task CreateNormalizesKeyAndPersistsOwnerMembershipAndEvent()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        await using var session = database.Open();

        // Act
        var result = await session.Create.Handle(new(" ab12 ", " Project ", "Notes"), default);
        await using var read = database.Open();
        var project = await read.Context.Projects.SingleAsync();

        // Assert
        Assert.Equal(OperationStatus.Success, result.Status);
        Assert.Equal("AB12", project.Key);
        Assert.Equal("Project", project.Name);
        Assert.Equal(1, project.Version);
        Assert.Equal("owner", (await read.Context.ProjectMembers.SingleAsync()).UserId);
        Assert.Equal("ProjectCreated", (await read.Context.ChangeEvents.SingleAsync()).Action);
        Assert.Equal(DateTimeKind.Utc, project.CreatedAtUtc.Kind);
        Assert.Single(await read.Repository.ListAccessibleAsync("owner", false, default));
        Assert.Empty(await read.Repository.ListAccessibleAsync("other", false, default));
        Assert.Null(await read.Repository.GetDetailsAsync(project.Id, "other", default));
        Assert.NotNull(await read.Repository.GetDetailsAsync(project.Id, "owner", default));
        var members = await read.Repository.GetMembersAsync(project.Id, "owner", default);
        Assert.NotNull(members);
        Assert.True(Assert.Single(members.Participants).IsOwner);
    }

    [Fact]
    public async Task CreateRejectsDuplicateNormalizedKeyWithoutPartialData()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        await using (var first = database.Open())
            await first.Create.Handle(new("AB", "First", ""), default);
        await using var second = database.Open("other");

        // Act
        var result = await second.Create.Handle(new("ab", "Second", ""), default);

        // Assert
        Assert.Equal(OperationStatus.Invalid, result.Status);
        Assert.Equal("Key", result.Field);
        Assert.Single(await second.Context.Projects.ToListAsync());
        Assert.Single(await second.Context.ProjectMembers.ToListAsync());
        Assert.Single(await second.Context.ChangeEvents.ToListAsync());
    }

    [Fact]
    public async Task EventFailureRollsBackAnAlreadyAllocatedProjectId()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        await using var session = database.Open(interceptor: new RejectProjectEvent());

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Create.Handle(new("AB", "Project", ""), default));
        await using var read = database.Open();

        // Assert
        Assert.Empty(await read.Context.Projects.ToListAsync());
        Assert.Empty(await read.Context.ProjectMembers.ToListAsync());
        Assert.Empty(await read.Context.ChangeEvents.ToListAsync());
    }

    [Fact]
    public async Task UpdateChecksVersionBeforeNoOpAndPreservesNewerValues()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        int id;
        await using (var first = database.Open()) id = (await first.Create.Handle(new("AB", "First", ""), default)).Value;
        await using (var second = database.Open())
            Assert.Equal(OperationStatus.Success, (await second.Update.Handle(new(id, 1, "Second", "Saved"), default)).Status);
        await using var stale = database.Open();

        // Act
        var result = await stale.Update.Handle(new(id, 1, "Second", "Saved"), default);
        var noOp = await stale.Update.Handle(new(id, 2, "Second", "Saved"), default);
        var project = await stale.Context.Projects.SingleAsync();

        // Assert
        Assert.Equal(OperationStatus.Conflict, result.Status);
        Assert.Equal(OperationStatus.Success, noOp.Status);
        Assert.Equal("Second", project.Name);
        Assert.Equal(2, project.Version);
        Assert.Equal(2, await stale.Context.ChangeEvents.CountAsync());
    }

    [Fact]
    public async Task MemberAndNonmemberCannotAdministerAProject()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        int id;
        await using (var owner = database.Open())
        {
            id = (await owner.Create.Handle(new("AB", "First", ""), default)).Value;
            await owner.AddMember.Handle(new(id, 1, "member@example.test"), default);
        }
        await using var member = database.Open("member");
        await using var other = database.Open("other");

        // Act
        var update = await member.Update.Handle(new(id, 2, "Changed", ""), default);
        var archive = await other.Archive.Handle(new(id, 2, true), default);

        // Assert
        Assert.Equal(OperationStatus.NotFound, update.Status);
        Assert.Equal(OperationStatus.NotFound, archive.Status);
        Assert.NotNull(await member.Repository.GetDetailsAsync(id, "member", default));
        Assert.Null(await member.Repository.GetMembersAsync(id, "member", default));
        Assert.Null(await other.Repository.GetDetailsAsync(id, "other", default));
        Assert.Equal(2, await member.Context.ChangeEvents.CountAsync());
    }

    [Fact]
    public async Task ArchiveIsReadOnlyAndRestoreRetainsTheProject()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        await using var session = database.Open();
        var id = (await session.Create.Handle(new("AB", "Project", "Notes"), default)).Value;

        // Act
        var archive = await session.Archive.Handle(new(id, 1, true), default);
        var edit = await session.Update.Handle(new(id, 2, "Changed", ""), default);
        var add = await session.AddMember.Handle(new(id, 2, "member@example.test"), default);
        var archived = await session.Repository.ListAccessibleAsync("owner", true, default);
        var restore = await session.Archive.Handle(new(id, 2, false), default);

        // Assert
        Assert.Equal(OperationStatus.Success, archive.Status);
        Assert.Equal(OperationStatus.Invalid, edit.Status);
        Assert.Equal(OperationStatus.Invalid, add.Status);
        Assert.Single(archived);
        Assert.Equal(OperationStatus.Success, restore.Status);
        var project = await session.Context.Projects.SingleAsync();
        Assert.Equal(id, project.Id);
        Assert.Equal("Notes", project.Description);
        Assert.Equal(3, project.Version);
        Assert.Equal(3, await session.Context.ChangeEvents.CountAsync());
    }

    [Fact]
    public async Task TransferKeepsBothParticipantsAndRevokeTakesEffectForCurrentReads()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        int id;
        await using (var owner = database.Open())
        {
            id = (await owner.Create.Handle(new("AB", "Project", ""), default)).Value;
            await owner.AddMember.Handle(new(id, 1, "member@example.test"), default);
            await owner.Archive.Handle(new(id, 2, true), default);
            Assert.Equal(OperationStatus.Success, (await owner.Transfer.Handle(new(id, 3, "member"), default)).Status);
        }
        await using var member = database.Open("member");

        // Act
        var removeOwner = await member.RemoveMember.Handle(new(id, 4, "member"), default);
        var removeFormer = await member.RemoveMember.Handle(new(id, 4, "owner"), default);
        await using var oldOwner = database.Open();

        // Assert
        Assert.Equal(OperationStatus.Invalid, removeOwner.Status);
        Assert.Equal(OperationStatus.Success, removeFormer.Status);
        Assert.Equal("member", (await member.Context.Projects.SingleAsync()).OwnerId);
        Assert.Equal("member", (await member.Context.ProjectMembers.SingleAsync()).UserId);
        Assert.Null(await oldOwner.Repository.GetDetailsAsync(id, "owner", default));
        Assert.Equal(OperationStatus.NotFound, (await oldOwner.Transfer.Handle(new(id, 5, "owner"), default)).Status);
    }

    [Fact]
    public async Task DuplicateMembershipAndNonmemberTransferDoNotWriteHistory()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        await using var session = database.Open();
        var id = (await session.Create.Handle(new("AB", "Project", ""), default)).Value;
        await session.AddMember.Handle(new(id, 1, "member@example.test"), default);

        // Act
        var duplicate = await session.AddMember.Handle(new(id, 2, "MEMBER@EXAMPLE.TEST"), default);
        var foreign = await session.Transfer.Handle(new(id, 2, "other"), default);
        var staleSelf = await session.Transfer.Handle(new(id, 1, "owner"), default);
        var self = await session.Transfer.Handle(new(id, 2, "owner"), default);

        // Assert
        Assert.Equal(OperationStatus.Invalid, duplicate.Status);
        Assert.Equal(OperationStatus.Invalid, foreign.Status);
        Assert.Equal(OperationStatus.Conflict, staleSelf.Status);
        Assert.Equal(OperationStatus.Success, self.Status);
        Assert.Equal(2, await session.Context.ChangeEvents.CountAsync());
        Assert.Equal(2, (await session.Context.Projects.SingleAsync()).Version);
    }

    [Fact]
    public async Task BusyWriterReturnsRetryableResultWithoutCreatingAProject()
    {
        // Arrange
        await using var database = new ProjectTestDatabase();
        await database.InitializeAsync();
        await using var writer = database.Open();
        await using var transaction = await writer.Unit.BeginWriteAsync(default);
        await using var competing = database.Open("other");

        // Act
        var result = await competing.Create.Handle(new("AB", "Project", ""), default);

        // Assert
        Assert.Equal(OperationStatus.Busy, result.Status);
        Assert.Empty(await competing.Context.Projects.ToListAsync());
    }
}
