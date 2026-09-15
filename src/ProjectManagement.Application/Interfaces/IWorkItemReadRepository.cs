using ProjectManagement.Application.WorkItems;
using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Application.Interfaces;

public interface IWorkItemReadRepository
{
    Task<WorkItemList?> ListAsync(int projectId, bool archived, int page, CancellationToken cancellationToken, WorkItemStatus? status = null);
    Task<WorkBoard?> GetBoardAsync(int projectId, CancellationToken cancellationToken);
    Task<WorkItemDetails?> GetDetailsAsync(int projectId, int workItemId, CancellationToken cancellationToken);
    Task<WorkItemOptions?> GetOptionsAsync(int projectId, CancellationToken cancellationToken);
    Task<ProjectLabels?> ListLabelsAsync(int projectId, CancellationToken cancellationToken);
}
