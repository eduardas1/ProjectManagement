using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;

namespace ProjectManagement.Application.WorkItems;

public record ChangeWorkItemHierarchyCommand(int ProjectId, int WorkItemId, long ExpectedVersion,
    int? ParentId, int? EpicId) : IRequest<OperationResult<int>>;

public class ChangeWorkItemHierarchyHandler(IWorkItemRepository repository, IProjectRepository projects,
    IProjectAccess access, IUnitOfWork unitOfWork, WorkItemValidation validation, ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<ChangeWorkItemHierarchyCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(ChangeWorkItemHierarchyCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var work = await repository.FindAsync(project.Id, request.WorkItemId, cancellationToken);
            if (work == null) return OperationResult<int>.NotFound();
            if (work.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived || work.IsArchived) return OperationResult<int>.Invalid("", "Restore this project and item before changing hierarchy.");
            var error = await validation.ValidateHierarchyAsync(work, request.ParentId, request.EpicId, cancellationToken);
            if (error != null) return error;
            if (work.ParentId == request.ParentId && work.EpicId == request.EpicId) return OperationResult<int>.Success(work.Id);
            if (work.Version == long.MaxValue) return OperationResult<int>.Invalid("", "This item's version limit has been reached.");
            var previous = new { work.ParentId, work.EpicId };
            work.ParentId = request.ParentId; work.EpicId = request.EpicId;
            var now = timeProvider.GetUtcNow().UtcDateTime;
            WorkItemChanges.Touch(work, now);
            WorkItemChanges.Record(projects, work, currentUser.UserId, now, "HierarchyChanged", "Work item hierarchy changed.",
                new { Previous = previous, Current = new { work.ParentId, work.EpicId } });
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
