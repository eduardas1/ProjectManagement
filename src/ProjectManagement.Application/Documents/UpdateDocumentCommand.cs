using ProjectManagement.Domain.Enums;
using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;

namespace ProjectManagement.Application.Documents;

public record UpdateDocumentCommand(int ProjectId, int DocumentId, long ExpectedVersion, string Title, string? Body, DocumentFormat Format = DocumentFormat.PlainText)
    : IRequest<OperationResult<int>>;

public class UpdateDocumentHandler(ICollaborationRepository repository, IProjectRepository projects,
    IProjectAccess access, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<UpdateDocumentCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(UpdateDocumentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var document = await repository.FindDocumentAsync(project.Id, request.DocumentId, cancellationToken);
            if (document == null) return OperationResult<int>.NotFound();
            if (document.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived || document.IsArchived) return OperationResult<int>.Invalid("", "Restore this project and document before editing it.");
            if (!Enum.IsDefined(request.Format)) return OperationResult<int>.Invalid("Format", "Choose a supported document format.");
            var title = (request.Title ?? "").Trim();
            var body = request.Body ?? "";
            if (title.Length is < 1 or > 200) return OperationResult<int>.Invalid("Title", "Enter a title of 1 to 200 characters.");
            if (body.Length > 100000) return OperationResult<int>.Invalid("Body", "Use at most 100000 characters for the document.");
            if (!Enum.IsDefined(document.Format)) return OperationResult<int>.Invalid("Format", "This document format is not supported. Preserve the source before editing.");
            if (document.Title == title && document.Body == body && document.Format == request.Format) return OperationResult<int>.Success(document.Id);
            if (document.Version == long.MaxValue) return OperationResult<int>.Invalid("", "This document's version limit has been reached.");
            document.Title = title; document.Body = body; document.Format = request.Format; document.Version++;
            document.UpdatedById = currentUser.UserId; document.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            DocumentChanges.SaveRevision(repository, document);
            DocumentChanges.Record(projects, document, "DocumentUpdated");
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
