using Workboard.Domain.Entities;
using Workboard.Domain.Enums;

namespace Workboard.Application.Interfaces;

public interface IWorkItemRepository
{
    Task<WorkItem?> FindAsync(int projectId, int workItemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkItem>> GetActiveDescendantsAsync(int projectId, int workItemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkItem>> GetActiveAssignedAsync(int projectId, string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkItemLabel>> GetLabelsAsync(int projectId, int workItemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkItemLink>> GetLinksAsync(int projectId, WorkItemLinkKind kind, CancellationToken cancellationToken);
    void Add(WorkItem workItem);
    void AddLabel(WorkItemLabel label);
    void RemoveLabel(WorkItemLabel label);
    void AddLink(WorkItemLink link);
    void RemoveLink(WorkItemLink link);
}
