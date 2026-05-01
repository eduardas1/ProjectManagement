using Microsoft.EntityFrameworkCore;
using Workboard.Application.Comments;
using Workboard.Application.Interfaces;
using Workboard.Application.WorkItems;
using Workboard.Domain.Entities;
using Workboard.Infrastructure.Data;

namespace Workboard.Infrastructure.Repositories;

public class CollaborationReadRepository(ApplicationDbContext context, ICurrentUser currentUser) : ICollaborationReadRepository
{
    public Task<CommentWork?> GetCommentWorkAsync(int projectId, int workItemId, CancellationToken cancellationToken)
        => context.WorkItems.AsNoTracking().Where(work => work.ProjectId == projectId && work.Id == workItemId
                && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId))
            .Join(context.Projects, work => work.ProjectId, project => project.Id, (work, project) => new CommentWork(
                new WorkProject(project.Id, project.Key, project.Name, project.IsArchived, project.OwnerId == currentUser.UserId),
                work.Id, work.Number, work.Title, work.IsArchived)).SingleOrDefaultAsync(cancellationToken);

    private IQueryable<Comment> CommentQuery(int projectId, int workItemId)
        => context.Comments.AsNoTracking().Where(comment => comment.ProjectId == projectId && comment.WorkItemId == workItemId
            && context.WorkItems.Any(work => work.ProjectId == projectId && work.Id == workItemId)
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId));

    private IQueryable<CommentItem> ProjectComments(IQueryable<Comment> query)
        => query.Select(comment => new CommentItem(comment.Id, comment.IsDeleted ? "" : comment.Body, comment.Version,
            context.Users.Where(user => user.Id == comment.AuthorId).Select(user => user.Email ?? user.UserName ?? "Account").Single(),
            comment.CreatedAtUtc, comment.UpdatedAtUtc, comment.IsDeleted,
            context.Users.Where(user => user.Id == comment.DeletedById).Select(user => user.Email ?? user.UserName ?? "Account").SingleOrDefault(),
            comment.DeletedAtUtc, comment.ModerationReason, comment.AuthorId == currentUser.UserId,
            !comment.IsDeleted && (comment.AuthorId == currentUser.UserId
                || context.Projects.Any(project => project.Id == comment.ProjectId && project.OwnerId == currentUser.UserId))));

    public async Task<WorkComments?> ListCommentsAsync(int projectId, int workItemId, int page, CancellationToken cancellationToken)
    {
        var work = await GetCommentWorkAsync(projectId, workItemId, cancellationToken);
        if (work == null) return null;
        var query = CommentQuery(projectId, workItemId);
        var total = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (total + 19) / 20);
        page = Math.Clamp(page, 1, pages);
        var items = await ProjectComments(query.OrderByDescending(comment => comment.CreatedAtUtc).ThenByDescending(comment => comment.Id)
            .Skip((page - 1) * 20).Take(20)).ToListAsync(cancellationToken);
        return new WorkComments(work, page, pages, total, items);
    }

    public async Task<WorkComment?> GetCommentAsync(int projectId, int workItemId, int commentId, CancellationToken cancellationToken)
    {
        var work = await GetCommentWorkAsync(projectId, workItemId, cancellationToken);
        if (work == null) return null;
        var comment = await ProjectComments(CommentQuery(projectId, workItemId).Where(comment => comment.Id == commentId))
            .SingleOrDefaultAsync(cancellationToken);
        return comment == null ? null : new WorkComment(work, comment);
    }
}
