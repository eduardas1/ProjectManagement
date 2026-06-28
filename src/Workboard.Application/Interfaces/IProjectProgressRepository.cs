using Workboard.Application.Common;
using Workboard.Application.Reports;
using Workboard.Application.Search;
using Workboard.Application.WorkItems;

namespace Workboard.Application.Interfaces;

public interface IProjectProgressRepository
{
    Task<WorkProject?> GetProjectAsync(int projectId, CancellationToken cancellationToken);
    Task<OperationResult<ProjectProgress>> GetProgressAsync(int projectId, WorkSearchInput input, CancellationToken cancellationToken);
}
