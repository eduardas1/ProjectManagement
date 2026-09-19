using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Application.WorkItems;

public record CreatedBoardWorkItem(int Id, bool TargetReached, WorkItemStatus Target);
public record CreateBoardWorkItemCommand(CreateWorkItemCommand Item, WorkItemStatus Target)
    : IRequest<OperationResult<CreatedBoardWorkItem>>;

public class CreateBoardWorkItemHandler(ISender sender)
    : IRequestHandler<CreateBoardWorkItemCommand, OperationResult<CreatedBoardWorkItem>>
{
    public async Task<OperationResult<CreatedBoardWorkItem>> Handle(CreateBoardWorkItemCommand request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Target))
            return OperationResult<CreatedBoardWorkItem>.Invalid("DesiredStatus", "Select a valid target status.");
        var created = await sender.Send(request.Item, cancellationToken);
        if (created.Status != OperationStatus.Success)
            return new(created.Status, null, created.Field, created.Error);
        WorkItemStatus[] steps = request.Target switch
        {
            WorkItemStatus.Backlog => [],
            WorkItemStatus.Cancelled => [WorkItemStatus.Cancelled],
            WorkItemStatus.Todo => [WorkItemStatus.Todo],
            WorkItemStatus.InProgress => [WorkItemStatus.Todo, WorkItemStatus.InProgress],
            WorkItemStatus.InReview => [WorkItemStatus.Todo, WorkItemStatus.InProgress, WorkItemStatus.InReview],
            WorkItemStatus.Done => [WorkItemStatus.Todo, WorkItemStatus.InProgress, WorkItemStatus.InReview, WorkItemStatus.Done],
            _ => []
        };
        long version = 1;
        foreach (var status in steps)
        {
            var changed = await sender.Send(new ChangeWorkItemStatusCommand(request.Item.ProjectId, created.Value, version, status), cancellationToken);
            if (changed.Status != OperationStatus.Success)
                return OperationResult<CreatedBoardWorkItem>.Success(new(created.Value, false, request.Target));
            version++;
        }
        return OperationResult<CreatedBoardWorkItem>.Success(new(created.Value, true, request.Target));
    }
}
