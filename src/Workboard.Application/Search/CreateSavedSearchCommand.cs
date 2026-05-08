using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;

namespace Workboard.Application.Search;

public record CreateSavedSearchCommand(int ProjectId, string Name, string Expression, string Ordering, bool Archived = false) : IRequest<OperationResult<int>>;

public class CreateSavedSearchHandler(ISavedSearchRepository repository, IProjectAccess access, IUnitOfWork unitOfWork,
    ICurrentUser currentUser, ISearchDefinitionValidator validator, TimeProvider timeProvider)
    : IRequestHandler<CreateSavedSearchCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(CreateSavedSearchCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before saving searches.");
            if (request.Archived) return OperationResult<int>.Invalid("", "Saved searches apply to active work. Switch off archived work first.");
            var name = (request.Name ?? "").Trim();
            if (name.Length is < 1 or > 100 || name.Any(char.IsControl))
                return OperationResult<int>.Invalid("Name", "Enter a name of up to 100 characters on one line.");
            var normalized = name.ToUpperInvariant();
            if (await repository.NameExistsAsync(project.Id, normalized, null, cancellationToken))
                return OperationResult<int>.Invalid("Name", "You already have a search with this name in this project.");
            var expression = (request.Expression ?? "").Trim();
            var ordering = (request.Ordering ?? "").Trim();
            var error = validator.ValidateExpression(expression);
            if (error != null) return OperationResult<int>.Invalid("Expression", error);
            error = validator.ValidateOrdering(ordering);
            if (error != null) return OperationResult<int>.Invalid("Ordering", error);
            ordering = ordering.Length == 0 ? "number" : string.Join(",", ordering.Split(',').Select(part => string.Join(" ", part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var filter = new SavedFilter { ProjectId = project.Id, OwnerId = currentUser.UserId, Name = name,
                NormalizedName = normalized, Expression = expression, Ordering = ordering, CreatedAtUtc = now, UpdatedAtUtc = now };
            repository.Add(filter);
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
