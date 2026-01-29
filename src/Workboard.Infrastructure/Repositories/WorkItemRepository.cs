using Microsoft.EntityFrameworkCore;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;
using Workboard.Domain.Enums;
using Workboard.Infrastructure.Data;

namespace Workboard.Infrastructure.Repositories;

public class WorkItemRepository(ApplicationDbContext context) : IWorkItemRepository
{
    public Task<WorkItem?> FindAsync(int projectId, int workItemId, CancellationToken cancellationToken)
        => context.WorkItems.SingleOrDefaultAsync(work => work.ProjectId == projectId && work.Id == workItemId, cancellationToken);

    public async Task<IReadOnlyList<WorkItem>> GetActiveDescendantsAsync(int projectId, int workItemId, CancellationToken cancellationToken)
        => await context.WorkItems.Where(work => work.ProjectId == projectId && !work.IsArchived
            && (work.ParentId == workItemId || work.EpicId == workItemId
                || work.ParentId != null && context.WorkItems.Any(parent => parent.ProjectId == projectId
                    && parent.Id == work.ParentId && parent.EpicId == workItemId)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkItem>> GetActiveAssignedAsync(int projectId, string userId, CancellationToken cancellationToken)
        => await context.WorkItems.Where(work => work.ProjectId == projectId && !work.IsArchived && work.AssigneeId == userId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkItemLabel>> GetLabelsAsync(int projectId, int workItemId, CancellationToken cancellationToken)
        => await context.WorkItemLabels.Where(label => label.ProjectId == projectId && label.WorkItemId == workItemId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkItemLink>> GetLinksAsync(int projectId, WorkItemLinkKind kind, CancellationToken cancellationToken)
        => await context.WorkItemLinks.Where(link => link.ProjectId == projectId && link.Kind == kind).ToListAsync(cancellationToken);

    public void Add(WorkItem workItem) => context.WorkItems.Add(workItem);
    public void AddLabel(WorkItemLabel label) => context.WorkItemLabels.Add(label);
    public void RemoveLabel(WorkItemLabel label) => context.WorkItemLabels.Remove(label);
    public void AddLink(WorkItemLink link) => context.WorkItemLinks.Add(link);
    public void RemoveLink(WorkItemLink link) => context.WorkItemLinks.Remove(link);
}
