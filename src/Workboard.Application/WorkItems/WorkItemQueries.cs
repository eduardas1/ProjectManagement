using MediatR;
using Workboard.Domain.Enums;
using Workboard.Application.Interfaces;

namespace Workboard.Application.WorkItems;

public record ListWorkItemsQuery(int ProjectId, bool Archived = false, int Page = 1, WorkItemStatus? Status = null) : IRequest<WorkItemList?>;
public record GetWorkItemQuery(int ProjectId, int WorkItemId) : IRequest<WorkItemDetails?>;
public record GetWorkItemOptionsQuery(int ProjectId) : IRequest<WorkItemOptions?>;
public record ListLabelsQuery(int ProjectId) : IRequest<ProjectLabels?>;

public class ListWorkItemsHandler(IWorkItemReadRepository repository) : IRequestHandler<ListWorkItemsQuery, WorkItemList?>
{
    public Task<WorkItemList?> Handle(ListWorkItemsQuery request, CancellationToken cancellationToken)
        => repository.ListAsync(request.ProjectId, request.Archived, request.Page, cancellationToken, request.Status);
}

public class GetWorkItemHandler(IWorkItemReadRepository repository) : IRequestHandler<GetWorkItemQuery, WorkItemDetails?>
{
    public Task<WorkItemDetails?> Handle(GetWorkItemQuery request, CancellationToken cancellationToken)
        => repository.GetDetailsAsync(request.ProjectId, request.WorkItemId, cancellationToken);
}

public class GetWorkItemOptionsHandler(IWorkItemReadRepository repository) : IRequestHandler<GetWorkItemOptionsQuery, WorkItemOptions?>
{
    public Task<WorkItemOptions?> Handle(GetWorkItemOptionsQuery request, CancellationToken cancellationToken)
        => repository.GetOptionsAsync(request.ProjectId, cancellationToken);
}

public class ListLabelsHandler(IWorkItemReadRepository repository) : IRequestHandler<ListLabelsQuery, ProjectLabels?>
{
    public Task<ProjectLabels?> Handle(ListLabelsQuery request, CancellationToken cancellationToken)
        => repository.ListLabelsAsync(request.ProjectId, cancellationToken);
}

public record GetWorkBoardQuery(int ProjectId) : IRequest<WorkBoard?>;

public class GetWorkBoardHandler(IWorkItemReadRepository repository) : IRequestHandler<GetWorkBoardQuery, WorkBoard?>
{
    public Task<WorkBoard?> Handle(GetWorkBoardQuery request, CancellationToken cancellationToken)
        => repository.GetBoardAsync(request.ProjectId, cancellationToken);
}
