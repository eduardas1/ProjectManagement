using Microsoft.EntityFrameworkCore;
using Workboard.Application.Interfaces;
using Workboard.Application.Projects;
using Workboard.Domain.Entities;
using Workboard.Infrastructure.Data;

namespace Workboard.Infrastructure.Repositories;

public class ProjectRepository(ApplicationDbContext context) : IProjectRepository
{
    public async Task<IReadOnlyList<ProjectSummary>> ListAccessibleAsync(string userId, bool archived, CancellationToken cancellationToken)
    {
        return await context.Projects.AsNoTracking()
            .Where(project => project.IsArchived == archived && context.ProjectMembers.Any(member => member.ProjectId == project.Id && member.UserId == userId))
            .OrderBy(project => project.Key)
            .Select(project => new ProjectSummary(project.Id, project.Key, project.Name, project.IsArchived, project.OwnerId == userId))
            .ToListAsync(cancellationToken);
    }

    public Task<ProjectDetails?> GetDetailsAsync(int projectId, string userId, CancellationToken cancellationToken)
    {
        return context.Projects.AsNoTracking()
            .Where(project => project.Id == projectId && context.ProjectMembers.Any(member => member.ProjectId == project.Id && member.UserId == userId))
            .Select(project => new ProjectDetails(project.Id, project.Key, project.Name, project.Description,
                context.Users.Where(user => user.Id == project.OwnerId).Select(user => user.Email!).Single(),
                project.IsArchived, project.Version, project.OwnerId == userId,
                context.ChangeEvents.Where(change => change.ProjectId == project.Id)
                    .OrderByDescending(change => change.OccurredAtUtc).ThenByDescending(change => change.Id).Take(50)
                    .Select(change => new ProjectHistory(context.Users.Where(user => user.Id == change.ActorId).Select(user => user.Email!).Single(),
                        change.OccurredAtUtc, change.Summary)).ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<ProjectMembers?> GetMembersAsync(int projectId, string ownerId, CancellationToken cancellationToken)
    {
        return context.Projects.AsNoTracking()
            .Where(project => project.Id == projectId && project.OwnerId == ownerId
                && context.ProjectMembers.Any(member => member.ProjectId == project.Id && member.UserId == ownerId))
            .Select(project => new ProjectMembers(project.Id, project.Key, project.Name, project.IsArchived, project.Version,
                context.ProjectMembers.Where(member => member.ProjectId == project.Id)
                    .OrderBy(member => member.UserId != ownerId).ThenBy(member => member.UserId)
                    .Select(member => new ProjectParticipant(member.UserId,
                        context.Users.Where(user => user.Id == member.UserId).Select(user => user.Email!).Single(),
                        member.UserId == ownerId, member.JoinedAtUtc)).ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<ProjectMember?> FindMemberAsync(int projectId, string userId, CancellationToken cancellationToken)
        => context.ProjectMembers.SingleOrDefaultAsync(member => member.ProjectId == projectId && member.UserId == userId, cancellationToken);

    public Task<string?> FindAccountIdAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return context.Users.Where(user => user.NormalizedEmail == normalized).Select(user => user.Id).SingleOrDefaultAsync(cancellationToken);
    }

    public Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken)
        => context.Projects.AnyAsync(project => project.Key == key, cancellationToken);

    public void Add(Project project) => context.Projects.Add(project);
    public void AddMember(ProjectMember member) => context.ProjectMembers.Add(member);
    public void RemoveMember(ProjectMember member) => context.ProjectMembers.Remove(member);
    public void AddEvent(ChangeEvent change) => context.ChangeEvents.Add(change);
}
