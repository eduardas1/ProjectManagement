using Workboard.Domain.Entities;

namespace Workboard.Application.Interfaces;

public interface ILabelRepository
{
    Task<Label?> FindAsync(int projectId, int labelId, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(int projectId, string normalizedName, int? excludingId, CancellationToken cancellationToken);
    Task<bool> AllExistAsync(int projectId, IReadOnlyList<int> labelIds, CancellationToken cancellationToken);
    Task<bool> IsInUseAsync(int projectId, int labelId, CancellationToken cancellationToken);
    void Add(Label label);
    void Remove(Label label);
}
