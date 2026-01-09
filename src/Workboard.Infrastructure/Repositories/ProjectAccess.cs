using Microsoft.EntityFrameworkCore;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;
using Workboard.Infrastructure.Data;

namespace Workboard.Infrastructure.Repositories;

public class ProjectAccess(ApplicationDbContext context, ICurrentUser currentUser) : IProjectAccess
{
    public Task<Project?> GetMemberProjectAsync(int projectId, CancellationToken cancellationToken)
        => context.Projects.SingleOrDefaultAsync(project => project.Id == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == project.Id && member.UserId == currentUser.UserId), cancellationToken);

    public Task<Project?> GetOwnerProjectAsync(int projectId, CancellationToken cancellationToken)
        => context.Projects.SingleOrDefaultAsync(project => project.Id == projectId && project.OwnerId == currentUser.UserId
            && context.ProjectMembers.Any(member => member.ProjectId == project.Id && member.UserId == currentUser.UserId), cancellationToken);
}
