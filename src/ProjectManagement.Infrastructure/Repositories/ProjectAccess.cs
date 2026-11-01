using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Infrastructure.Repositories;

public class ProjectAccess(ApplicationDbContext context, ICurrentUser currentUser) : IProjectAccess
{
    public Task<Project?> GetMemberProjectAsync(int projectId, CancellationToken cancellationToken)
        => context.Projects.SingleOrDefaultAsync(project => project.Id == projectId && !project.IsArchived
            && context.ProjectMembers.Any(member => member.ProjectId == project.Id && member.UserId == currentUser.UserId), cancellationToken);

    public Task<Project?> GetOwnerProjectAsync(int projectId, CancellationToken cancellationToken)
        => context.Projects.SingleOrDefaultAsync(project => project.Id == projectId && !project.IsArchived && project.OwnerId == currentUser.UserId
            && context.ProjectMembers.Any(member => member.ProjectId == project.Id && member.UserId == currentUser.UserId), cancellationToken);
}
