using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;
using Workboard.Application.Search;

namespace Workboard.Application.Reports;

public record GetProjectProgressQuery(int ProjectId, WorkSearchInput Input) : IRequest<OperationResult<ProjectProgress>>;

public class GetProjectProgressHandler(IProjectProgressRepository repository)
    : IRequestHandler<GetProjectProgressQuery, OperationResult<ProjectProgress>>
{
    public Task<OperationResult<ProjectProgress>> Handle(GetProjectProgressQuery request, CancellationToken cancellationToken)
        => repository.GetProgressAsync(request.ProjectId, request.Input, cancellationToken);
}
