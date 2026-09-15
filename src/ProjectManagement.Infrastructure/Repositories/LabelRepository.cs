using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Infrastructure.Repositories;

public class LabelRepository(ApplicationDbContext context) : ILabelRepository
{
    public Task<Label?> FindAsync(int projectId, int labelId, CancellationToken cancellationToken)
        => context.Labels.SingleOrDefaultAsync(label => label.ProjectId == projectId && label.Id == labelId, cancellationToken);

    public Task<bool> NameExistsAsync(int projectId, string normalizedName, int? excludingId, CancellationToken cancellationToken)
        => context.Labels.AnyAsync(label => label.ProjectId == projectId && label.NormalizedName == normalizedName
            && (excludingId == null || label.Id != excludingId), cancellationToken);

    public async Task<bool> AllExistAsync(int projectId, IReadOnlyList<int> labelIds, CancellationToken cancellationToken)
        => await context.Labels.CountAsync(label => label.ProjectId == projectId && labelIds.Contains(label.Id), cancellationToken) == labelIds.Distinct().Count();

    public Task<bool> IsInUseAsync(int projectId, int labelId, CancellationToken cancellationToken)
        => context.WorkItemLabels.AnyAsync(label => label.ProjectId == projectId && label.LabelId == labelId, cancellationToken);

    public void Add(Label label) => context.Labels.Add(label);
    public void Remove(Label label) => context.Labels.Remove(label);
}
