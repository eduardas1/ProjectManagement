using MediatR;
using ProjectManagement.Application.Interfaces;

namespace ProjectManagement.Application.Projects;

public record ListProjectsQuery() : IRequest<IReadOnlyList<ProjectSummary>>;
public record GetProjectQuery(int ProjectId) : IRequest<ProjectDetails?>;
public record GetProjectMembersQuery(int ProjectId) : IRequest<ProjectMembers?>;

public class ListProjectsHandler(IProjectRepository repository, ICurrentUser currentUser)
    : IRequestHandler<ListProjectsQuery, IReadOnlyList<ProjectSummary>>
{
    public Task<IReadOnlyList<ProjectSummary>> Handle(ListProjectsQuery request, CancellationToken cancellationToken)
        => repository.ListAccessibleAsync(currentUser.UserId, cancellationToken);
}

public class GetProjectHandler(IProjectRepository repository, ICurrentUser currentUser)
    : IRequestHandler<GetProjectQuery, ProjectDetails?>
{
    public Task<ProjectDetails?> Handle(GetProjectQuery request, CancellationToken cancellationToken)
        => repository.GetDetailsAsync(request.ProjectId, currentUser.UserId, cancellationToken);
}

public class GetProjectMembersHandler(IProjectRepository repository, ICurrentUser currentUser)
    : IRequestHandler<GetProjectMembersQuery, ProjectMembers?>
{
    public Task<ProjectMembers?> Handle(GetProjectMembersQuery request, CancellationToken cancellationToken)
        => repository.GetMembersAsync(request.ProjectId, currentUser.UserId, cancellationToken);
}
