using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;

namespace Workboard.Application.Search;

public record RenameSavedSearchCommand(int ProjectId, int Id, long ExpectedVersion, string Name) : IRequest<OperationResult<int>>;

public class RenameSavedSearchHandler(ISavedSearchRepository repository, IProjectAccess access, IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRequestHandler<RenameSavedSearchCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(RenameSavedSearchCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var filter = await repository.FindOwnAsync(project.Id, request.Id, cancellationToken);
            if (filter == null) return OperationResult<int>.NotFound();
            if (filter.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before renaming searches.");
            var name = (request.Name ?? "").Trim();
            if (name.Length is < 1 or > 100 || name.Any(char.IsControl))
                return OperationResult<int>.Invalid("Name", "Enter a name of up to 100 characters on one line.");
            if (await repository.NameExistsAsync(project.Id, name.ToUpperInvariant(), filter.Id, cancellationToken))
                return OperationResult<int>.Invalid("Name", "You already have a search with this name in this project.");
            if (filter.Name == name) return OperationResult<int>.Success(filter.Id);
            if (filter.Version == long.MaxValue) return OperationResult<int>.Invalid("", "This search's version limit has been reached.");
            filter.Name = name; filter.NormalizedName = name.ToUpperInvariant(); filter.Version++;
            filter.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
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
