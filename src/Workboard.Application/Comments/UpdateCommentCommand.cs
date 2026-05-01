using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;

namespace Workboard.Application.Comments;

public record UpdateCommentCommand(int ProjectId, int WorkItemId, int CommentId, long ExpectedVersion, string Body) : IRequest<OperationResult<int>>;

public class UpdateCommentHandler(ICollaborationRepository repository, IWorkItemRepository workItems,
    IProjectRepository projects, IProjectAccess access, IUnitOfWork unitOfWork, ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<UpdateCommentCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(UpdateCommentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var work = await workItems.FindAsync(project.Id, request.WorkItemId, cancellationToken);
            if (work == null) return OperationResult<int>.NotFound();
            var comment = await repository.FindCommentAsync(project.Id, work.Id, request.CommentId, cancellationToken);
            if (comment == null || comment.AuthorId != currentUser.UserId) return OperationResult<int>.NotFound();
            if (comment.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived || work.IsArchived) return OperationResult<int>.Invalid("", "Restore this project and item before editing a comment.");
            if (comment.IsDeleted) return OperationResult<int>.Invalid("Body", "This comment has been removed.");
            var body = (request.Body ?? "").Trim();
            if (body.Length is < 1 or > 5000) return OperationResult<int>.Invalid("Body", "Enter a comment of 1 to 5000 characters.");
            if (comment.Body == body) return OperationResult<int>.Success(comment.Id);
            if (comment.Version == long.MaxValue) return OperationResult<int>.Invalid("", "This comment's version limit has been reached.");
            var now = timeProvider.GetUtcNow().UtcDateTime;
            comment.Body = body; comment.Version++; comment.UpdatedAtUtc = now;
            CommentChanges.Record(projects, comment, currentUser.UserId, now, "CommentUpdated");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<int>.Success(comment.Id);
        }
        catch (WriteException exception)
        {
            return exception.Failure == WriteFailure.Busy ? OperationResult<int>.Busy() : OperationResult<int>.Conflict();
        }
    }
}
