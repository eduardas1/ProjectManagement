using ProjectManagement.Application.Projects;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Application.Interfaces;

public interface IProjectRepository
{
    Task<IReadOnlyList<ProjectSummary>> ListAccessibleAsync(string userId, CancellationToken cancellationToken);
    Task<ProjectDetails?> GetDetailsAsync(int projectId, string userId, CancellationToken cancellationToken);
    Task<ProjectMembers?> GetMembersAsync(int projectId, string ownerId, CancellationToken cancellationToken);
    Task<ProjectMember?> FindMemberAsync(int projectId, string userId, CancellationToken cancellationToken);
    Task<string?> FindAccountIdAsync(string email, CancellationToken cancellationToken);
    Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken);
    void Add(Project project);
    void AddMember(ProjectMember member);
    void RemoveMember(ProjectMember member);
    void AddEvent(ChangeEvent change);
}
