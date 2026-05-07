using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;

namespace Workboard.Application.Attachments;

public record UploadAttachmentCommand(int ProjectId, int? WorkItemId, int? DocumentId, string DisplayName, byte[] Contents)
    : IRequest<OperationResult<int>>;

public class UploadAttachmentHandler(ICollaborationRepository repository, IWorkItemRepository workItems,
    IProjectRepository projects, IProjectAccess access, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<UploadAttachmentCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(UploadAttachmentCommand request, CancellationToken cancellationToken)
    {
        var validation = AttachmentFile.Validate(request.DisplayName, request.Contents);
        if (validation.Status != OperationStatus.Success) return OperationResult<int>.Invalid(validation.Field!, validation.Error!);
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            if (request.WorkItemId.HasValue == request.DocumentId.HasValue)
                return OperationResult<int>.Invalid("", "Choose one work item or document.");
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before uploading a file.");
            if (request.WorkItemId.HasValue)
            {
                var work = await workItems.FindAsync(project.Id, request.WorkItemId.Value, cancellationToken);
                if (work == null) return OperationResult<int>.NotFound();
                if (work.IsArchived) return OperationResult<int>.Invalid("", "Restore this work item before uploading a file.");
            }
            else
            {
                var document = await repository.FindDocumentAsync(project.Id, request.DocumentId!.Value, cancellationToken);
                if (document == null) return OperationResult<int>.NotFound();
                if (document.IsArchived) return OperationResult<int>.Invalid("", "Restore this document before uploading a file.");
            }
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var attachment = new Attachment
            {
                ProjectId = project.Id, WorkItemId = request.WorkItemId, DocumentId = request.DocumentId,
                StoredName = Guid.NewGuid().ToString("N"), DisplayName = request.DisplayName, ContentType = validation.Value!,
                Length = request.Contents.Length, Contents = request.Contents, UploadedById = currentUser.UserId, UploadedAtUtc = now
            };
            repository.AddAttachment(attachment);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            AttachmentChanges.Record(projects, attachment, currentUser.UserId, now, "AttachmentUploaded");
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
