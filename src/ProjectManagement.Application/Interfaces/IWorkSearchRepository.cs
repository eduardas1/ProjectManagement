using ProjectManagement.Application.Common;
using ProjectManagement.Application.Search;

namespace ProjectManagement.Application.Interfaces;

public interface IWorkSearchRepository
{
    Task<WorkSearchOptions?> GetOptionsAsync(int projectId, CancellationToken cancellationToken);
    Task<OperationResult<WorkSearchPage>> SearchAsync(int projectId, WorkSearchInput input, CancellationToken cancellationToken);
}
