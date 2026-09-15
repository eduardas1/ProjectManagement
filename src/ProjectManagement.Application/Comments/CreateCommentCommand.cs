using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Application.Comments;

public record CreateCommentCommand(int ProjectId, int WorkItemId, string Body) : IRequest<OperationResult<int>>;

public class CreateCommentHandler(ICollaborationRepository repository, IWorkItemRepository workItems,
    IProjectRepository projects, IProjectAccess access, IUnitOfWork unitOfWork, ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<CreateCommentCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(CreateCommentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var work = await workItems.FindAsync(project.Id, request.WorkItemId, cancellationToken);
            if (work == null) return OperationResult<int>.NotFound();
            if (project.IsArchived || work.IsArchived) return OperationResult<int>.Invalid("", "Restore this project and item before adding a comment.");
            var body = (request.Body ?? "").Trim();
            if (body.Length is < 1 or > 5000) return OperationResult<int>.Invalid("Body", "Enter a comment of 1 to 5000 characters.");
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var comment = new Comment { ProjectId = project.Id, WorkItemId = work.Id, AuthorId = currentUser.UserId,
                Body = body, CreatedAtUtc = now };
            repository.AddComment(comment);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            CommentChanges.Record(projects, comment, currentUser.UserId, now, "CommentCreated");
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
