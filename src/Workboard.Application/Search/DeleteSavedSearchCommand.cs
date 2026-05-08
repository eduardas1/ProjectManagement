using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;

namespace Workboard.Application.Search;

public record DeleteSavedSearchCommand(int ProjectId, int Id, long ExpectedVersion) : IRequest<OperationResult<int>>;

public class DeleteSavedSearchHandler(ISavedSearchRepository repository, IProjectAccess access, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteSavedSearchCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(DeleteSavedSearchCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var filter = await repository.FindOwnAsync(project.Id, request.Id, cancellationToken);
            if (filter == null) return OperationResult<int>.NotFound();
            if (filter.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before deleting searches.");
            repository.Remove(filter);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<int>.Success(filter.Id);
        }
        catch (WriteException exception)
        {
            return exception.Failure == WriteFailure.Busy ? OperationResult<int>.Busy() : OperationResult<int>.Conflict();
        }
    }
}
