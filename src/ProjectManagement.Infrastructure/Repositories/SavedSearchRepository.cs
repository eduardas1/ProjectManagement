using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Application.Search;
using ProjectManagement.Application.WorkItems;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Infrastructure.Repositories;

public class SavedSearchRepository(ApplicationDbContext context, ICurrentUser currentUser) : ISavedSearchRepository
{
    private IQueryable<SavedFilter> OwnQuery(int projectId)
        => context.SavedFilters.Where(filter => filter.ProjectId == projectId && filter.OwnerId == currentUser.UserId
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId));

    private Task<WorkProject?> ProjectAsync(int projectId, CancellationToken cancellationToken)
        => context.Projects.AsNoTracking().Where(project => project.Id == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId))
            .Select(project => new WorkProject(project.Id, project.Key, project.Name, project.IsArchived, project.OwnerId == currentUser.UserId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<SavedSearchList?> ListAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await ProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        var items = await OwnQuery(projectId).AsNoTracking().OrderBy(filter => filter.NormalizedName).ThenBy(filter => filter.Id)
            .Select(filter => new SavedSearchItem(filter.Id, filter.Name, filter.Version)).ToListAsync(cancellationToken);
        return new SavedSearchList(project, items);
    }

    public async Task<SavedSearchDetails?> GetAsync(int projectId, int id, CancellationToken cancellationToken)
    {
        var project = await ProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        return await OwnQuery(projectId).AsNoTracking().Where(filter => filter.Id == id)
            .Select(filter => new SavedSearchDetails(project, filter.Id, filter.Name, filter.Expression, filter.Ordering, filter.Version))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<SavedFilter?> FindOwnAsync(int projectId, int id, CancellationToken cancellationToken)
        => OwnQuery(projectId).SingleOrDefaultAsync(filter => filter.Id == id, cancellationToken);

    public Task<bool> NameExistsAsync(int projectId, string normalizedName, int? excludingId, CancellationToken cancellationToken)
        => OwnQuery(projectId).AnyAsync(filter => filter.NormalizedName == normalizedName
            && (excludingId == null || filter.Id != excludingId), cancellationToken);

    public void Add(SavedFilter filter) => context.SavedFilters.Add(filter);
    public void Remove(SavedFilter filter) => context.SavedFilters.Remove(filter);
}
