using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;

namespace Workboard.Application.Documents;

public record CreateDocumentCommand(int ProjectId, string Title, string? Body) : IRequest<OperationResult<int>>;

public class CreateDocumentHandler(ICollaborationRepository repository, IProjectRepository projects,
    IProjectAccess access, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<CreateDocumentCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(CreateDocumentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before adding a document.");
            var title = (request.Title ?? "").Trim();
            var body = request.Body ?? "";
            if (title.Length is < 1 or > 200) return OperationResult<int>.Invalid("Title", "Enter a title of 1 to 200 characters.");
            if (body.Length > 100000) return OperationResult<int>.Invalid("Body", "Use at most 100000 characters for the document.");
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var document = new ProjectDocument { ProjectId = project.Id, Title = title, Body = body,
                CreatedById = currentUser.UserId, UpdatedById = currentUser.UserId, CreatedAtUtc = now, UpdatedAtUtc = now };
            repository.AddDocument(document);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            DocumentChanges.SaveRevision(repository, document);
            DocumentChanges.Record(projects, document, "DocumentCreated");
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
