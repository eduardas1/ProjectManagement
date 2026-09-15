using Gridify;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Application.Search;
using ProjectManagement.Application.WorkItems;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Infrastructure.Search;

internal record WorkSearchSelection(WorkProject Project, string Expression, string Ordering, IQueryable<WorkItem> Items);

internal class WorkSearchQuery(ApplicationDbContext context, ICurrentUser currentUser,
    ISearchDefinitionValidator validator, WorkItemSearchMapper mapper)
{
    public Task<WorkProject?> GetProjectAsync(int projectId, CancellationToken cancellationToken)
        => context.Projects.AsNoTracking().Where(project => project.Id == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == project.Id && member.UserId == currentUser.UserId))
            .Select(project => new WorkProject(project.Id, project.Key, project.Name, project.IsArchived, project.OwnerId == currentUser.UserId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<OperationResult<WorkSearchSelection>> SelectAsync(int projectId, WorkSearchInput input, CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (project == null) return OperationResult<WorkSearchSelection>.NotFound();
        var definition = WorkSearchDefinition.Build(project.Key, input);
        if (definition.Status != OperationStatus.Success)
            return OperationResult<WorkSearchSelection>.Invalid(definition.Field ?? "", definition.Error!);
        var expression = definition.Value!;
        var error = validator.ValidateExpression(expression);
        if (error != null) return OperationResult<WorkSearchSelection>.Invalid(nameof(input.Expression), error);
        var ordering = (input.Ordering ?? "").Trim();
        error = validator.ValidateOrdering(ordering);
        if (error != null) return OperationResult<WorkSearchSelection>.Invalid(nameof(input.Ordering), error);
        ordering = ordering.Length == 0 ? "number" : string.Join(",", ordering.Split(',').Select(part => string.Join(" ", part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
        var query = context.WorkItems.AsNoTracking().Where(work => work.ProjectId == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == work.ProjectId && member.UserId == currentUser.UserId)
            && work.IsArchived == input.Archived).ApplyFiltering(expression, mapper);
        return OperationResult<WorkSearchSelection>.Success(new WorkSearchSelection(project, expression, ordering, query));
    }
}
