using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Application.Search;
using ProjectManagement.Application.WorkItems;

namespace ProjectManagement.Application.Reports;

public record GetProjectProgressQuery(int ProjectId, WorkSearchInput Input) : IRequest<OperationResult<ProjectProgress>>;

public class GetProjectProgressHandler(IProjectProgressRepository repository)
    : IRequestHandler<GetProjectProgressQuery, OperationResult<ProjectProgress>>
{
    public Task<OperationResult<ProjectProgress>> Handle(GetProjectProgressQuery request, CancellationToken cancellationToken)
        => repository.GetProgressAsync(request.ProjectId, request.Input, cancellationToken);
}

public record GetReportProjectQuery(int ProjectId) : IRequest<WorkProject?>;

public class GetReportProjectHandler(IProjectProgressRepository repository) : IRequestHandler<GetReportProjectQuery, WorkProject?>
{
    public Task<WorkProject?> Handle(GetReportProjectQuery request, CancellationToken cancellationToken)
        => repository.GetProjectAsync(request.ProjectId, cancellationToken);
}
