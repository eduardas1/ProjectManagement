using ProjectManagement.Application.Common;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Application.WorkItems;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Domain.Enums;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Infrastructure.Repositories;

public class WorkItemReadRepository(ApplicationDbContext context, ICurrentUser currentUser) : IWorkItemReadRepository
{
    private readonly IQueryable<PersonSummary> people = PersonQueries.Select(context);

    private IQueryable<WorkItem> WorkQuery(int projectId)
        => context.WorkItems.AsNoTracking().Where(work => work.ProjectId == projectId && !work.IsArchived
            && context.Projects.Any(project => project.Id == projectId && !project.IsArchived)
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId));

    private IQueryable<Label> LabelQuery(int projectId)
        => context.Labels.AsNoTracking().Where(label => label.ProjectId == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId));

    private Task<WorkProject?> GetProjectAsync(int projectId, CancellationToken cancellationToken)
        => context.Projects.AsNoTracking().Where(project => project.Id == projectId && !project.IsArchived
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId))
            .Select(project => new WorkProject(project.Id, project.Key, project.Name, project.IsArchived, project.OwnerId == currentUser.UserId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<WorkItemList?> ListAsync(int projectId, int page, CancellationToken cancellationToken, WorkItemStatus? status = null, TableOptions? table = null)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value)) throw new ArgumentOutOfRangeException(nameof(status));
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        var query = WorkQuery(projectId);
        if (status.HasValue) query = query.Where(work => work.Status == status.Value);
        var total = await query.CountAsync(cancellationToken);
        table = (table ?? new()).Normalize("number", "number", "title", "type", "status", "priority", "assignee", "deadline");
        var pages = table.Pages(total);
        page = Math.Clamp(page, 1, pages);
        var ordered = table.Sort switch
        {
            "title" => query.SortBy(work => work.Title, table.Descending),
            "type" => query.SortBy(work => work.Type, table.Descending),
            "status" => query.SortBy(work => work.Status, table.Descending),
            "priority" => query.SortBy(work => work.Priority, table.Descending),
            "deadline" => query.SortBy(work => work.DueDate, table.Descending),
            "assignee" => query.SortBy(work => people.Where(user => user.Id == work.AssigneeId)
                .Select(user => user.FirstName + " " + user.LastName + " " + user.Email).FirstOrDefault(), table.Descending),
            _ => query.SortBy(work => work.Number, table.Descending)
        };
        var items = await ordered.ThenBy(work => work.Id).Skip((page - 1) * table.PageSize).Take(table.PageSize)
            .Select(work => new WorkItemSummary(work.Id, work.Number, work.Title, work.Type, work.Status, work.Priority,
                (work.AssigneeId == null ? null : new PersonSummary(work.AssigneeId,
                people.Where(user => user.Id == work.AssigneeId).Select(user => user.FirstName).FirstOrDefault() ?? "",
                people.Where(user => user.Id == work.AssigneeId).Select(user => user.LastName).FirstOrDefault() ?? "",
                people.Where(user => user.Id == work.AssigneeId).Select(user => user.Email).FirstOrDefault() ?? "")), work.DueDate, work.Version))
            .ToListAsync(cancellationToken);
        return new WorkItemList(project, page, pages, total, items, status, table);
    }

    public async Task<WorkBoard?> GetBoardAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        var items = await WorkQuery(projectId).Where(work => !work.IsArchived
                && (work.Status == WorkItemStatus.Todo || work.Status == WorkItemStatus.InProgress
                    || work.Status == WorkItemStatus.InReview || work.Status == WorkItemStatus.Done))
            .OrderBy(work => work.Number).ThenBy(work => work.Id)
            .Select(work => new WorkItemSummary(work.Id, work.Number, work.Title, work.Type, work.Status, work.Priority,
                (work.AssigneeId == null ? null : new PersonSummary(work.AssigneeId,
                people.Where(user => user.Id == work.AssigneeId).Select(user => user.FirstName).FirstOrDefault() ?? "",
                people.Where(user => user.Id == work.AssigneeId).Select(user => user.LastName).FirstOrDefault() ?? "",
                people.Where(user => user.Id == work.AssigneeId).Select(user => user.Email).FirstOrDefault() ?? "")), work.DueDate, work.Version))
            .ToListAsync(cancellationToken);
        var columns = new[] { WorkItemStatus.Todo, WorkItemStatus.InProgress, WorkItemStatus.InReview, WorkItemStatus.Done }
            .Select(status =>
            {
                var matches = items.Where(work => work.Status == status).ToList();
                return new WorkBoardColumn(status, matches.Count, matches.Take(20).ToList());
            }).ToList();
        return new WorkBoard(project, columns);
    }

    public async Task<WorkItemOptions?> GetOptionsAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        var members = await context.ProjectMembers.AsNoTracking().Where(member => member.ProjectId == projectId
                && context.ProjectMembers.Any(current => current.ProjectId == projectId && current.UserId == currentUser.UserId))
            .Join(people, member => member.UserId, user => user.Id, (member, user) => new { member.UserId, user.Email, user.FirstName, user.LastName })
            .OrderBy(member => member.Email).Select(member => new WorkAccount(member.UserId, member.Email, member.FirstName, member.LastName)).ToListAsync(cancellationToken);
        var labels = await GetLabelsAsync(projectId, cancellationToken);
        var items = await WorkQuery(projectId).Where(work => !work.IsArchived).OrderBy(work => work.Number)
            .Select(work => new RelatedWork(work.Id, work.Number, work.Title, work.Type, work.IsArchived)).ToListAsync(cancellationToken);
        return new WorkItemOptions(project, members, labels, items);
    }

    private async Task<IReadOnlyList<LabelSummary>> GetLabelsAsync(int projectId, CancellationToken cancellationToken)
        => await LabelQuery(projectId).OrderBy(label => label.NormalizedName).Select(label => new LabelSummary(label.Id,
            label.Name, label.Version, context.WorkItemLabels.Count(item => item.ProjectId == projectId && item.LabelId == label.Id)))
            .ToListAsync(cancellationToken);

    public async Task<ProjectLabels?> ListLabelsAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        return project == null ? null : new ProjectLabels(project, await GetLabelsAsync(projectId, cancellationToken));
    }

    public async Task<WorkItemDetails?> GetDetailsAsync(int projectId, int workItemId, CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        var work = await WorkQuery(projectId).SingleOrDefaultAsync(work => work.Id == workItemId, cancellationToken);
        if (work == null) return null;
        var creator = await people.Where(user => user.Id == work.CreatedById).SingleAsync(cancellationToken);
        var assignee = work.AssigneeId == null ? null : await people.Where(user => user.Id == work.AssigneeId).SingleAsync(cancellationToken);
        var parent = work.ParentId == null ? null : await WorkQuery(projectId).SingleOrDefaultAsync(item => item.Id == work.ParentId, cancellationToken);
        var epicId = work.EpicId ?? parent?.EpicId;
        var epic = epicId == null ? null : await WorkQuery(projectId).Where(item => item.Id == epicId)
            .Select(item => new RelatedWork(item.Id, item.Number, item.Title, item.Type, item.IsArchived)).SingleOrDefaultAsync(cancellationToken);
        var children = await WorkQuery(projectId).Where(item => item.ParentId == work.Id || item.EpicId == work.Id)
            .OrderBy(item => item.Number).Select(item => new RelatedWork(item.Id, item.Number, item.Title, item.Type, item.IsArchived))
            .ToListAsync(cancellationToken);
        var labels = await LabelQuery(projectId).Where(label => context.WorkItemLabels.Any(item => item.ProjectId == projectId
                && item.WorkItemId == work.Id && item.LabelId == label.Id)).OrderBy(label => label.NormalizedName)
            .Select(label => new LabelSummary(label.Id, label.Name, label.Version,
                context.WorkItemLabels.Count(item => item.ProjectId == projectId && item.LabelId == label.Id))).ToListAsync(cancellationToken);
        var connections = await context.WorkItemLinks.AsNoTracking().Where(link => link.ProjectId == projectId
                && (link.SourceId == work.Id || link.TargetId == work.Id)
                && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId))
            .Join(WorkQuery(projectId), link => link.SourceId == work.Id ? link.TargetId : link.SourceId, item => item.Id,
                (link, item) => new { Link = link, Item = item })
            .OrderBy(row => row.Item.Number).ThenBy(row => row.Link.Kind)
            .Select(row => new WorkItemConnection(row.Item.Id, row.Item.Number, row.Item.Title, row.Item.IsArchived,
                row.Link.Kind, row.Link.SourceId == work.Id)).ToListAsync(cancellationToken);
        var history = await context.ChangeEvents.AsNoTracking().Where(change => change.ProjectId == projectId && change.WorkItemId == work.Id
                && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId))
            .OrderByDescending(change => change.OccurredAtUtc).ThenByDescending(change => change.Id).Take(50)
            .Select(change => new WorkHistory(new PersonSummary(change.ActorId,
                people.Where(user => user.Id == change.ActorId).Select(user => user.FirstName).Single(),
                people.Where(user => user.Id == change.ActorId).Select(user => user.LastName).Single(),
                people.Where(user => user.Id == change.ActorId).Select(user => user.Email).Single()),
                change.OccurredAtUtc, change.Summary, change.DetailsJson)).ToListAsync(cancellationToken);
        return new WorkItemDetails(project, work, creator, assignee,
            parent == null ? null : new RelatedWork(parent.Id, parent.Number, parent.Title, parent.Type, parent.IsArchived),
            epic, children, labels, connections, history);
    }
}
