using Workboard.Application.Common;
using Workboard.Application.Reports;
using Workboard.Application.Search;

namespace Workboard.Application.Interfaces;

public interface IProjectProgressRepository
{
    Task<OperationResult<ProjectProgress>> GetProgressAsync(int projectId, WorkSearchInput input, CancellationToken cancellationToken);
}
