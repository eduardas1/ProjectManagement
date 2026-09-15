using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Application.WorkItems;

public record RemoveWorkItemLinkCommand(int ProjectId, int WorkItemId, long ExpectedVersion,
    int TargetId, WorkItemLinkKind Kind) : IRequest<OperationResult<int>>;

public class RemoveWorkItemLinkHandler(IWorkItemRepository repository, IProjectRepository projects,
    IProjectAccess access, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<RemoveWorkItemLinkCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(RemoveWorkItemLinkCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var work = await repository.FindAsync(project.Id, request.WorkItemId, cancellationToken);
            if (work == null) return OperationResult<int>.NotFound();
            if (work.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (!Enum.IsDefined(request.Kind)) return OperationResult<int>.Invalid("Kind", "Select a valid relationship.");
            var target = await repository.FindAsync(project.Id, request.TargetId, cancellationToken);
            if (target == null) return OperationResult<int>.Invalid("TargetId", "Select an item from this project.");
            if (project.IsArchived || work.IsArchived || target.IsArchived)
                return OperationResult<int>.Invalid("", "Restore this project and both items before changing relationships.");
            var link = (await repository.GetLinksAsync(project.Id, request.Kind, cancellationToken))
                .SingleOrDefault(link => link.SourceId == work.Id && link.TargetId == target.Id);
            if (link == null) return OperationResult<int>.Invalid("", "This relationship no longer exists.");
            if (work.Version == long.MaxValue || target.Version == long.MaxValue)
                return OperationResult<int>.Invalid("", "An item's version limit has been reached.");
            repository.RemoveLink(link);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            WorkItemChanges.Touch(work, now); WorkItemChanges.Touch(target, now);
            WorkItemChanges.Record(projects, work, currentUser.UserId, now, "LinkRemoved", request.Kind == WorkItemLinkKind.Blocks
                ? $"No longer blocks {project.Key}-{target.Number}." : $"No longer duplicates {project.Key}-{target.Number}.", new { request.Kind, TargetId = target.Id });
            WorkItemChanges.Record(projects, target, currentUser.UserId, now, "LinkRemoved", request.Kind == WorkItemLinkKind.Blocks
                ? $"Is no longer blocked by {project.Key}-{work.Number}." : $"Is no longer duplicated by {project.Key}-{work.Number}.", new { request.Kind, SourceId = work.Id });
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
