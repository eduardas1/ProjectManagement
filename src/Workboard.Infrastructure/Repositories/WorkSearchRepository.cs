using Gridify;
using Microsoft.EntityFrameworkCore;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;
using Workboard.Application.Search;
using Workboard.Application.WorkItems;
using Workboard.Domain.Entities;
using Workboard.Infrastructure.Data;
using Workboard.Infrastructure.Search;

namespace Workboard.Infrastructure.Repositories;

public class WorkSearchRepository(ApplicationDbContext context, ICurrentUser currentUser,
    ISearchDefinitionValidator validator, WorkItemSearchMapper mapper) : IWorkSearchRepository
{
    private Task<WorkProject?> GetProjectAsync(int projectId, CancellationToken cancellationToken)
        => context.Projects.AsNoTracking().Where(project => project.Id == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == project.Id && member.UserId == currentUser.UserId))
            .Select(project => new WorkProject(project.Id, project.Key, project.Name, project.IsArchived, project.OwnerId == currentUser.UserId))
            .SingleOrDefaultAsync(cancellationToken);

    private IQueryable<WorkItem> WorkQuery(int projectId)
        => context.WorkItems.AsNoTracking().Where(work => work.ProjectId == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == work.ProjectId && member.UserId == currentUser.UserId));

    public async Task<WorkSearchOptions?> GetOptionsAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        var members = await context.ProjectMembers.AsNoTracking().Where(member => member.ProjectId == projectId
                && context.ProjectMembers.Any(current => current.ProjectId == projectId && current.UserId == currentUser.UserId))
            .Join(context.Users, member => member.UserId, user => user.Id, (member, user) => new { Id = member.UserId, user.Email })
            .OrderBy(member => member.Email).ThenBy(member => member.Id)
            .Select(member => new WorkAccount(member.Id, member.Email!)).ToListAsync(cancellationToken);
        var labels = await context.Labels.AsNoTracking().Where(label => label.ProjectId == projectId
                && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId))
            .OrderBy(label => label.NormalizedName).ThenBy(label => label.Id).Select(label => label.Name).ToListAsync(cancellationToken);
        return new WorkSearchOptions(project, members, labels);
    }

    public async Task<OperationResult<WorkSearchPage>> SearchAsync(int projectId, WorkSearchInput input, CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (project == null) return OperationResult<WorkSearchPage>.NotFound();
        var definition = WorkSearchDefinition.Build(project.Key, input);
        if (definition.Status != OperationStatus.Success)
            return OperationResult<WorkSearchPage>.Invalid(definition.Field ?? "", definition.Error!);
        var expression = definition.Value!;
        var error = validator.ValidateExpression(expression);
        if (error != null) return OperationResult<WorkSearchPage>.Invalid(nameof(input.Expression), error);
        var ordering = (input.Ordering ?? "").Trim();
        error = validator.ValidateOrdering(ordering);
        if (error != null) return OperationResult<WorkSearchPage>.Invalid(nameof(input.Ordering), error);
        ordering = ordering.Length == 0 ? "number" : string.Join(",", ordering.Split(',').Select(part => string.Join(" ", part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
        var query = WorkQuery(projectId).Where(work => work.IsArchived == input.Archived).ApplyFiltering(expression, mapper);
        var total = await query.CountAsync(cancellationToken);
        var pages = (int)Math.Max(1, (total + (long)input.PageSize - 1) / input.PageSize);
        var page = Math.Min(input.Page, pages);
        var ordered = ((IOrderedQueryable<WorkItem>)query.ApplyOrdering(ordering, mapper)).ThenBy(work => work.Id);
        var items = await ordered.Skip(checked((page - 1) * input.PageSize)).Take(input.PageSize)
            .Select(work => new WorkItemSummary(work.Id, work.Number, work.Title, work.Type, work.Status, work.Priority,
                context.Users.Where(user => user.Id == work.AssigneeId).Select(user => user.Email).SingleOrDefault(), work.DueDate))
            .ToListAsync(cancellationToken);
        return OperationResult<WorkSearchPage>.Success(new WorkSearchPage(project, expression, ordering,
            input.Archived, page, input.PageSize, pages, total, items));
    }
}
