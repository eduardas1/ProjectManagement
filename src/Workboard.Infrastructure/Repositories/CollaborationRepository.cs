using Microsoft.EntityFrameworkCore;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;
using Workboard.Infrastructure.Data;

namespace Workboard.Infrastructure.Repositories;

public class CollaborationRepository(ApplicationDbContext context) : ICollaborationRepository
{
    public Task<Comment?> FindCommentAsync(int projectId, int workItemId, int commentId, CancellationToken cancellationToken)
        => context.Comments.SingleOrDefaultAsync(comment => comment.ProjectId == projectId
            && comment.WorkItemId == workItemId && comment.Id == commentId, cancellationToken);

    public void AddComment(Comment comment) => context.Comments.Add(comment);
}
