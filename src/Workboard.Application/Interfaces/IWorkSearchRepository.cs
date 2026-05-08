using Workboard.Application.Common;
using Workboard.Application.Search;

namespace Workboard.Application.Interfaces;

public interface IWorkSearchRepository
{
    Task<WorkSearchOptions?> GetOptionsAsync(int projectId, CancellationToken cancellationToken);
    Task<OperationResult<WorkSearchPage>> SearchAsync(int projectId, WorkSearchInput input, CancellationToken cancellationToken);
}
