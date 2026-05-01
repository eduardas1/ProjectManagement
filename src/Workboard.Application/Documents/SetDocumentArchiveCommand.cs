using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;

namespace Workboard.Application.Documents;

public record SetDocumentArchiveCommand(int ProjectId, int DocumentId, long ExpectedVersion, bool IsArchived)
    : IRequest<OperationResult<int>>;

public class SetDocumentArchiveHandler(ICollaborationRepository repository, IProjectRepository projects,
    IProjectAccess access, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<SetDocumentArchiveCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(SetDocumentArchiveCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetOwnerProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var document = await repository.FindDocumentAsync(project.Id, request.DocumentId, cancellationToken);
            if (document == null) return OperationResult<int>.NotFound();
            if (document.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before changing its document archive.");
            if (document.IsArchived == request.IsArchived) return OperationResult<int>.Success(document.Id);
            if (document.Version == long.MaxValue) return OperationResult<int>.Invalid("", "This document's version limit has been reached.");
            document.IsArchived = request.IsArchived; document.Version++;
            document.UpdatedById = currentUser.UserId; document.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            DocumentChanges.Record(projects, document, request.IsArchived ? "DocumentArchived" : "DocumentRestored");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<int>.Success(document.Id);
        }
        catch (WriteException exception)
        {
            return exception.Failure == WriteFailure.Busy ? OperationResult<int>.Busy() : OperationResult<int>.Conflict();
        }
    }
}
