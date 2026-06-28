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
    private readonly WorkSearchQuery search = new(context, currentUser, validator, mapper);

    public async Task<WorkSearchOptions?> GetOptionsAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await search.GetProjectAsync(projectId, cancellationToken);
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
        var result = await search.SelectAsync(projectId, input, cancellationToken);
        if (result.Status != OperationStatus.Success)
            return new OperationResult<WorkSearchPage>(result.Status, null, result.Field, result.Error);
        var selection = result.Value!;
        var query = selection.Items;
        var ordering = selection.Ordering;
        var total = await query.CountAsync(cancellationToken);
        var pages = (int)Math.Max(1, (total + (long)input.PageSize - 1) / input.PageSize);
        var page = Math.Min(input.Page, pages);
        var ordered = ((IOrderedQueryable<WorkItem>)query.ApplyOrdering(ordering, mapper)).ThenBy(work => work.Id);
        var items = await ordered.Skip(checked((page - 1) * input.PageSize)).Take(input.PageSize)
            .Select(work => new WorkItemSummary(work.Id, work.Number, work.Title, work.Type, work.Status, work.Priority,
                context.Users.Where(user => user.Id == work.AssigneeId).Select(user => user.Email).SingleOrDefault(), work.DueDate))
            .ToListAsync(cancellationToken);
        return OperationResult<WorkSearchPage>.Success(new WorkSearchPage(selection.Project, selection.Expression, ordering,
            input.Archived, page, input.PageSize, pages, total, items));
    }
}
