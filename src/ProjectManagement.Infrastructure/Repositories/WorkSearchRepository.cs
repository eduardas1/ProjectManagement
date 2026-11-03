using Gridify;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Application.Search;
using ProjectManagement.Application.WorkItems;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Infrastructure.Search;

namespace ProjectManagement.Infrastructure.Repositories;

public class WorkSearchRepository(ApplicationDbContext context, ICurrentUser currentUser,
    ISearchDefinitionValidator validator, WorkItemSearchMapper mapper) : IWorkSearchRepository
{
    private readonly IQueryable<PersonSummary> people = PersonQueries.Select(context);

    private readonly WorkSearchQuery search = new(context, currentUser, validator, mapper);

    public async Task<WorkSearchOptions?> GetOptionsAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await search.GetProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        var members = await context.ProjectMembers.AsNoTracking().Where(member => member.ProjectId == projectId
                && context.ProjectMembers.Any(current => current.ProjectId == projectId && current.UserId == currentUser.UserId))
            .Join(people, member => member.UserId, user => user.Id, (member, user) => new { Id = member.UserId, user.Email, user.FirstName, user.LastName })
            .OrderBy(member => member.Email).ThenBy(member => member.Id)
            .Select(member => new WorkAccount(member.Id, member.Email, member.FirstName, member.LastName)).ToListAsync(cancellationToken);
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
        var orderingMapper = new WorkItemSearchMapper();
        orderingMapper.AddMap("orderAssignee", work => people.Where(user => user.Id == work.AssigneeId)
            .Select(user => user.FirstName + " " + user.LastName + " " + user.Email).FirstOrDefault());
        var mappedOrdering = string.Join(",", ordering.Split(',').Select(part => part.StartsWith("assignee", StringComparison.Ordinal)
            ? "orderAssignee" + part[8..] : part));
        var ordered = ((IOrderedQueryable<WorkItem>)query.ApplyOrdering(mappedOrdering, orderingMapper)).ThenBy(work => work.Id);
        var items = await ordered.Skip(checked((page - 1) * input.PageSize)).Take(input.PageSize)
            .Select(work => new WorkItemSummary(work.Id, work.Number, work.Title, work.Type, work.Status, work.Priority,
                (work.AssigneeId == null ? null : new PersonSummary(work.AssigneeId,
                people.Where(user => user.Id == work.AssigneeId).Select(user => user.FirstName).FirstOrDefault() ?? "",
                people.Where(user => user.Id == work.AssigneeId).Select(user => user.LastName).FirstOrDefault() ?? "",
                people.Where(user => user.Id == work.AssigneeId).Select(user => user.Email).FirstOrDefault() ?? "")), work.DueDate, work.Version))
            .ToListAsync(cancellationToken);
        return OperationResult<WorkSearchPage>.Success(new WorkSearchPage(selection.Project, selection.Expression, ordering,
            page, input.PageSize, pages, total, items));
    }
}
