using Workboard.Application.Comments;

namespace Workboard.Application.Interfaces;

public interface ICollaborationReadRepository
{
    Task<CommentWork?> GetCommentWorkAsync(int projectId, int workItemId, CancellationToken cancellationToken);
    Task<WorkComments?> ListCommentsAsync(int projectId, int workItemId, int page, CancellationToken cancellationToken);
    Task<WorkComment?> GetCommentAsync(int projectId, int workItemId, int commentId, CancellationToken cancellationToken);
}
