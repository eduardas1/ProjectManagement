using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;

namespace ProjectManagement.Application.WorkItems;

public record SetWorkItemArchiveCommand(int ProjectId, int WorkItemId, long ExpectedVersion, bool IsArchived) : IRequest<OperationResult<int>>;

public class SetWorkItemArchiveHandler(IWorkItemRepository repository, IProjectRepository projects,
    IProjectAccess access, IUnitOfWork unitOfWork, WorkItemValidation validation, ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<SetWorkItemArchiveCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(SetWorkItemArchiveCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetOwnerProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var work = await repository.FindAsync(project.Id, request.WorkItemId, cancellationToken);
            if (work == null) return OperationResult<int>.NotFound();
            if (work.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before changing work archival.");
            if (work.IsArchived == request.IsArchived) return OperationResult<int>.Success(work.Id);
            if (work.Version == long.MaxValue) return OperationResult<int>.Invalid("", "This item's version limit has been reached.");
            var previousAssignee = work.AssigneeId;
            if (request.IsArchived)
            {
                if ((await repository.GetActiveDescendantsAsync(project.Id, work.Id, cancellationToken)).Count > 0)
                    return OperationResult<int>.Invalid("", "Archive or move active descendants before archiving this item.");
            }
            else
            {
                var error = await validation.ValidateHierarchyAsync(work, work.ParentId, work.EpicId, cancellationToken);
                if (error != null) return error;
                if (work.AssigneeId != null && await projects.FindMemberAsync(project.Id, work.AssigneeId, cancellationToken) == null)
                    work.AssigneeId = null;
            }
            work.IsArchived = request.IsArchived;
            var now = timeProvider.GetUtcNow().UtcDateTime;
            WorkItemChanges.Touch(work, now);
            WorkItemChanges.Record(projects, work, currentUser.UserId, now, request.IsArchived ? "WorkArchived" : "WorkRestored",
                request.IsArchived ? "Work item archived." : "Work item restored.", new { work.IsArchived, PreviousAssigneeId = previousAssignee, work.AssigneeId });
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
