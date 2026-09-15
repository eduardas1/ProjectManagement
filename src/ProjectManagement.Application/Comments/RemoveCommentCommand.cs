using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;

namespace ProjectManagement.Application.Comments;

public record RemoveCommentCommand(int ProjectId, int WorkItemId, int CommentId, long ExpectedVersion,
    string? ModerationReason) : IRequest<OperationResult<int>>;

public class RemoveCommentHandler(ICollaborationRepository repository, IWorkItemRepository workItems,
    IProjectRepository projects, IProjectAccess access, IUnitOfWork unitOfWork, ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<RemoveCommentCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(RemoveCommentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var work = await workItems.FindAsync(project.Id, request.WorkItemId, cancellationToken);
            if (work == null) return OperationResult<int>.NotFound();
            var comment = await repository.FindCommentAsync(project.Id, work.Id, request.CommentId, cancellationToken);
            if (comment == null || (comment.AuthorId != currentUser.UserId && project.OwnerId != currentUser.UserId))
                return OperationResult<int>.NotFound();
            if (comment.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived || work.IsArchived) return OperationResult<int>.Invalid("", "Restore this project and item before removing a comment.");
            var reason = (request.ModerationReason ?? "").Trim();
            if (reason.Length > 500) return OperationResult<int>.Invalid("ModerationReason", "Use at most 500 characters for the reason.");
            if (comment.AuthorId != currentUser.UserId && reason.Length == 0)
                return OperationResult<int>.Invalid("ModerationReason", "Enter a reason for removing another author's comment.");
            if (comment.IsDeleted) return OperationResult<int>.Success(comment.Id);
            if (comment.Version == long.MaxValue) return OperationResult<int>.Invalid("", "This comment's version limit has been reached.");
            var now = timeProvider.GetUtcNow().UtcDateTime;
            comment.Body = ""; comment.Version++; comment.IsDeleted = true; comment.DeletedById = currentUser.UserId;
            comment.DeletedAtUtc = now; comment.UpdatedAtUtc = now; comment.ModerationReason = reason.Length == 0 ? null : reason;
            CommentChanges.Record(projects, comment, currentUser.UserId, now, "CommentRemoved");
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
