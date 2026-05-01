using MediatR;
using Workboard.Application.Interfaces;

namespace Workboard.Application.Comments;

public record GetCommentWorkQuery(int ProjectId, int WorkItemId) : IRequest<CommentWork?>;
public record ListCommentsQuery(int ProjectId, int WorkItemId, int Page = 1) : IRequest<WorkComments?>;
public record GetCommentQuery(int ProjectId, int WorkItemId, int CommentId) : IRequest<WorkComment?>;

public class GetCommentWorkHandler(ICollaborationReadRepository repository) : IRequestHandler<GetCommentWorkQuery, CommentWork?>
{
    public Task<CommentWork?> Handle(GetCommentWorkQuery request, CancellationToken cancellationToken)
        => repository.GetCommentWorkAsync(request.ProjectId, request.WorkItemId, cancellationToken);
}

public class ListCommentsHandler(ICollaborationReadRepository repository) : IRequestHandler<ListCommentsQuery, WorkComments?>
{
    public Task<WorkComments?> Handle(ListCommentsQuery request, CancellationToken cancellationToken)
        => repository.ListCommentsAsync(request.ProjectId, request.WorkItemId, request.Page, cancellationToken);
}

public class GetCommentHandler(ICollaborationReadRepository repository) : IRequestHandler<GetCommentQuery, WorkComment?>
{
    public Task<WorkComment?> Handle(GetCommentQuery request, CancellationToken cancellationToken)
        => repository.GetCommentAsync(request.ProjectId, request.WorkItemId, request.CommentId, cancellationToken);
}
