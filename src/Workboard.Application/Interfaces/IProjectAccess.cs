using Workboard.Domain.Entities;

namespace Workboard.Application.Interfaces;

public interface IProjectAccess
{
    Task<Project?> GetMemberProjectAsync(int projectId, CancellationToken cancellationToken);
    Task<Project?> GetOwnerProjectAsync(int projectId, CancellationToken cancellationToken);
}
