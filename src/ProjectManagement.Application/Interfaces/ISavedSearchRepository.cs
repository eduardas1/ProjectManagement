using ProjectManagement.Application.Search;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Application.Interfaces;

public interface ISavedSearchRepository
{
    Task<SavedSearchList?> ListAsync(int projectId, CancellationToken cancellationToken);
    Task<SavedSearchDetails?> GetAsync(int projectId, int id, CancellationToken cancellationToken);
    Task<SavedFilter?> FindOwnAsync(int projectId, int id, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(int projectId, string normalizedName, int? excludingId, CancellationToken cancellationToken);
    void Add(SavedFilter filter);
    void Remove(SavedFilter filter);
}
