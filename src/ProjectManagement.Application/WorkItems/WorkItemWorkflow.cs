using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Application.WorkItems;

public static class WorkItemWorkflow
{
    public static IReadOnlyList<WorkItemStatus> AllowedStatuses(WorkItemStatus current) => current switch
    {
        WorkItemStatus.Backlog => [WorkItemStatus.Todo, WorkItemStatus.Cancelled],
        WorkItemStatus.Todo => [WorkItemStatus.Backlog, WorkItemStatus.InProgress, WorkItemStatus.Cancelled],
        WorkItemStatus.InProgress => [WorkItemStatus.Todo, WorkItemStatus.InReview, WorkItemStatus.Cancelled],
        WorkItemStatus.InReview => [WorkItemStatus.InProgress, WorkItemStatus.Done, WorkItemStatus.Cancelled],
        WorkItemStatus.Done => [WorkItemStatus.Todo],
        WorkItemStatus.Cancelled => [WorkItemStatus.Backlog],
        _ => []
    };

    public static bool CanTransition(WorkItemStatus current, WorkItemStatus next)
        => Enum.IsDefined(next) && AllowedStatuses(current).Contains(next);

    public static bool CanComplete(IEnumerable<WorkItemStatus> descendants)
        => descendants.All(WorkItemChanges.IsClosed);
}
