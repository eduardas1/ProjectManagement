using MediatR;
using Workboard.Application.Interfaces;
using Workboard.Application.WorkItems;

namespace Workboard.Application.Search;

public record SavedSearchItem(int Id, string Name, long Version);
public record SavedSearchList(WorkProject Project, IReadOnlyList<SavedSearchItem> Items);
public record SavedSearchDetails(WorkProject Project, int Id, string Name, string Expression, string Ordering, long Version);
public record ListSavedSearchesQuery(int ProjectId) : IRequest<SavedSearchList?>;
public record GetSavedSearchQuery(int ProjectId, int Id) : IRequest<SavedSearchDetails?>;

public class ListSavedSearchesHandler(ISavedSearchRepository repository) : IRequestHandler<ListSavedSearchesQuery, SavedSearchList?>
{
    public Task<SavedSearchList?> Handle(ListSavedSearchesQuery request, CancellationToken cancellationToken)
        => repository.ListAsync(request.ProjectId, cancellationToken);
}

public class GetSavedSearchHandler(ISavedSearchRepository repository) : IRequestHandler<GetSavedSearchQuery, SavedSearchDetails?>
{
    public Task<SavedSearchDetails?> Handle(GetSavedSearchQuery request, CancellationToken cancellationToken)
        => repository.GetAsync(request.ProjectId, request.Id, cancellationToken);
}
