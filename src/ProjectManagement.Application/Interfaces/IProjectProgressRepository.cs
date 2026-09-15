using ProjectManagement.Application.Common;
using ProjectManagement.Application.Reports;
using ProjectManagement.Application.Search;
using ProjectManagement.Application.WorkItems;

namespace ProjectManagement.Application.Interfaces;

public interface IProjectProgressRepository
{
    Task<WorkProject?> GetProjectAsync(int projectId, CancellationToken cancellationToken);
    Task<OperationResult<ProjectProgress>> GetProgressAsync(int projectId, WorkSearchInput input, CancellationToken cancellationToken);
}
