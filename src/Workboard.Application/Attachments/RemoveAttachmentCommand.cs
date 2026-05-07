using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;

namespace Workboard.Application.Attachments;

public record RemoveAttachmentCommand(int ProjectId, int AttachmentId, long ExpectedVersion) : IRequest<OperationResult<int>>;

public class RemoveAttachmentHandler(ICollaborationRepository repository, IWorkItemRepository workItems,
    IProjectRepository projects, IProjectAccess access, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<RemoveAttachmentCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(RemoveAttachmentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var attachment = await repository.FindAttachmentAsync(project.Id, request.AttachmentId, cancellationToken);
            if (attachment == null || (attachment.UploadedById != currentUser.UserId && project.OwnerId != currentUser.UserId))
                return OperationResult<int>.NotFound();
            if (attachment.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before removing a file.");
            if (attachment.WorkItemId.HasValue)
            {
                var work = await workItems.FindAsync(project.Id, attachment.WorkItemId.Value, cancellationToken);
                if (work == null) return OperationResult<int>.NotFound();
                if (work.IsArchived) return OperationResult<int>.Invalid("", "Restore this work item before removing a file.");
            }
            else
            {
                var document = await repository.FindDocumentAsync(project.Id, attachment.DocumentId!.Value, cancellationToken);
                if (document == null) return OperationResult<int>.NotFound();
                if (document.IsArchived) return OperationResult<int>.Invalid("", "Restore this document before removing a file.");
            }
            repository.RemoveAttachment(attachment);
            AttachmentChanges.Record(projects, attachment, currentUser.UserId, timeProvider.GetUtcNow().UtcDateTime, "AttachmentRemoved");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<int>.Success(attachment.Id);
        }
        catch (WriteException exception)
        {
            return exception.Failure == WriteFailure.Busy ? OperationResult<int>.Busy() : OperationResult<int>.Conflict();
        }
    }
}
