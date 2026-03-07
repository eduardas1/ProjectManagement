using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;
using Workboard.Domain.Enums;

namespace Workboard.Application.WorkItems;

public record ChangeWorkItemStatusCommand(int ProjectId, int WorkItemId, long ExpectedVersion,
    WorkItemStatus Status) : IRequest<OperationResult<int>>;

public class ChangeWorkItemStatusHandler(IWorkItemRepository repository, IProjectRepository projects,
    IProjectAccess access, IUnitOfWork unitOfWork, WorkItemValidation validation, ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<ChangeWorkItemStatusCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(ChangeWorkItemStatusCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var work = await repository.FindAsync(project.Id, request.WorkItemId, cancellationToken);
            if (work == null) return OperationResult<int>.NotFound();
            if (work.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived || work.IsArchived)
                return OperationResult<int>.Invalid("", "Restore this project and item before changing status.");
            if (!Enum.IsDefined(request.Status)) return OperationResult<int>.Invalid("Status", "Select a valid status.");
            if (work.Status == request.Status) return OperationResult<int>.Success(work.Id);
            if (!WorkItemWorkflow.CanTransition(work.Status, request.Status))
                return OperationResult<int>.Invalid("Status", "This status transition is not allowed.");
            if (work.Version == long.MaxValue) return OperationResult<int>.Invalid("", "This item's version limit has been reached.");
            if (request.Status == WorkItemStatus.Done)
            {
                var descendants = await repository.GetActiveDescendantsAsync(project.Id, work.Id, cancellationToken);
                if (!WorkItemWorkflow.CanComplete(descendants.Select(item => item.Status)))
                    return OperationResult<int>.Invalid("Status", "Finish or cancel active descendants before completing this item.");
            }
            var proposed = new WorkItem { Id = work.Id, ProjectId = project.Id, Type = work.Type,
                Status = request.Status, ParentId = work.ParentId, EpicId = work.EpicId };
            var error = await validation.ValidateHierarchyAsync(proposed, work.ParentId, work.EpicId, cancellationToken);
            if (error != null) return OperationResult<int>.Invalid("Status", error.Error ?? "This hierarchy does not allow that status.");

            var previous = work.Status;
            work.Status = request.Status;
            var now = timeProvider.GetUtcNow().UtcDateTime;
            WorkItemChanges.Touch(work, now);
            WorkItemChanges.Record(projects, work, currentUser.UserId, now, "StatusChanged",
                $"Status changed from {previous} to {work.Status}.", new { Previous = previous, Current = work.Status });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<int>.Success(work.Id);
        }
        catch (WriteException exception)
        {
            return exception.Failure == WriteFailure.Busy ? OperationResult<int>.Busy() : OperationResult<int>.Conflict();
        }
    }
}
