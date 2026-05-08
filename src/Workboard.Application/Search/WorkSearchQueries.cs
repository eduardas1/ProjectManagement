using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;

namespace Workboard.Application.Search;

public record GetWorkSearchOptionsQuery(int ProjectId) : IRequest<WorkSearchOptions?>;
public record SearchWorkItemsQuery(int ProjectId, WorkSearchInput Input) : IRequest<OperationResult<WorkSearchPage>>;

public class GetWorkSearchOptionsHandler(IWorkSearchRepository repository) : IRequestHandler<GetWorkSearchOptionsQuery, WorkSearchOptions?>
{
    public Task<WorkSearchOptions?> Handle(GetWorkSearchOptionsQuery request, CancellationToken cancellationToken)
        => repository.GetOptionsAsync(request.ProjectId, cancellationToken);
}

public class SearchWorkItemsHandler(IWorkSearchRepository repository) : IRequestHandler<SearchWorkItemsQuery, OperationResult<WorkSearchPage>>
{
    public Task<OperationResult<WorkSearchPage>> Handle(SearchWorkItemsQuery request, CancellationToken cancellationToken)
        => repository.SearchAsync(request.ProjectId, request.Input, cancellationToken);
}
