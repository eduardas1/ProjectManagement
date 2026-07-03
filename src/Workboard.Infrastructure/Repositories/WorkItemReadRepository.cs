using Microsoft.EntityFrameworkCore;
using Workboard.Application.Interfaces;
using Workboard.Application.WorkItems;
using Workboard.Domain.Entities;
using Workboard.Domain.Enums;
using Workboard.Infrastructure.Data;

namespace Workboard.Infrastructure.Repositories;

public class WorkItemReadRepository(ApplicationDbContext context, ICurrentUser currentUser) : IWorkItemReadRepository
{
    private IQueryable<WorkItem> WorkQuery(int projectId)
        => context.WorkItems.AsNoTracking().Where(work => work.ProjectId == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId));

    private IQueryable<Label> LabelQuery(int projectId)
        => context.Labels.AsNoTracking().Where(label => label.ProjectId == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId));

    private Task<WorkProject?> GetProjectAsync(int projectId, CancellationToken cancellationToken)
        => context.Projects.AsNoTracking().Where(project => project.Id == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId))
            .Select(project => new WorkProject(project.Id, project.Key, project.Name, project.IsArchived, project.OwnerId == currentUser.UserId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<WorkItemList?> ListAsync(int projectId, bool archived, int page, CancellationToken cancellationToken, WorkItemStatus? status = null)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value)) throw new ArgumentOutOfRangeException(nameof(status));
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        var query = WorkQuery(projectId).Where(work => work.IsArchived == archived);
        if (status.HasValue) query = query.Where(work => work.Status == status.Value);
        var total = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (total + 19) / 20);
        page = Math.Clamp(page, 1, pages);
        var items = await query.OrderBy(work => work.Number).ThenBy(work => work.Id).Skip((page - 1) * 20).Take(20)
            .Select(work => new WorkItemSummary(work.Id, work.Number, work.Title, work.Type, work.Status, work.Priority,
                context.Users.Where(user => user.Id == work.AssigneeId).Select(user => user.Email).SingleOrDefault(), work.DueDate, work.Version))
            .ToListAsync(cancellationToken);
        return new WorkItemList(project, archived, page, pages, total, items, status);
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
                context.Users.Where(user => user.Id == work.AssigneeId).Select(user => user.Email).SingleOrDefault(), work.DueDate, work.Version))
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
            .Join(context.Users, member => member.UserId, user => user.Id, (member, user) => new { member.UserId, user.Email })
            .OrderBy(member => member.Email).Select(member => new WorkAccount(member.UserId, member.Email!)).ToListAsync(cancellationToken);
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
        var creator = await context.Users.Where(user => user.Id == work.CreatedById).Select(user => user.Email!).SingleAsync(cancellationToken);
        var assignee = work.AssigneeId == null ? null : await context.Users.Where(user => user.Id == work.AssigneeId).Select(user => user.Email).SingleAsync(cancellationToken);
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
            .Select(change => new WorkHistory(context.Users.Where(user => user.Id == change.ActorId).Select(user => user.Email!).Single(),
                change.OccurredAtUtc, change.Summary, change.DetailsJson)).ToListAsync(cancellationToken);
        return new WorkItemDetails(project, work, creator, assignee,
            parent == null ? null : new RelatedWork(parent.Id, parent.Number, parent.Title, parent.Type, parent.IsArchived),
            epic, children, labels, connections, history);
    }
}
