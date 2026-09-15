using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Application.WorkItems;

public record AddWorkItemLinkCommand(int ProjectId, int WorkItemId, long ExpectedVersion,
    int TargetId, WorkItemLinkKind Kind) : IRequest<OperationResult<int>>;

public class AddWorkItemLinkHandler(IWorkItemRepository repository, IProjectRepository projects,
    IProjectAccess access, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<AddWorkItemLinkCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(AddWorkItemLinkCommand request, CancellationToken cancellationToken)
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
            if (work.Id == target.Id) return OperationResult<int>.Invalid("TargetId", "An item cannot link to itself.");
            var links = await repository.GetLinksAsync(project.Id, request.Kind, cancellationToken);
            if (links.Any(link => link.SourceId == work.Id && link.TargetId == target.Id))
                return OperationResult<int>.Invalid("TargetId", "This relationship already exists.");
            if (request.Kind == WorkItemLinkKind.Duplicates && links.Any(link => link.SourceId == work.Id))
                return OperationResult<int>.Invalid("TargetId", "Remove the existing duplicate relationship before selecting another item.");
            if (WorkItemGraph.CreatesCycle(links, work.Id, target.Id))
                return OperationResult<int>.Invalid("TargetId", "This relationship would create a directed cycle.");
            if (work.Version == long.MaxValue || target.Version == long.MaxValue)
                return OperationResult<int>.Invalid("", "An item's version limit has been reached.");
            var now = timeProvider.GetUtcNow().UtcDateTime;
            repository.AddLink(new WorkItemLink { ProjectId = project.Id, SourceId = work.Id, TargetId = target.Id,
                Kind = request.Kind, CreatedById = currentUser.UserId, CreatedAtUtc = now });
            WorkItemChanges.Touch(work, now); WorkItemChanges.Touch(target, now);
            WorkItemChanges.Record(projects, work, currentUser.UserId, now, "LinkAdded", request.Kind == WorkItemLinkKind.Blocks
                ? $"Blocks {project.Key}-{target.Number}." : $"Duplicates {project.Key}-{target.Number}.", new { request.Kind, TargetId = target.Id });
            WorkItemChanges.Record(projects, target, currentUser.UserId, now, "LinkAdded", request.Kind == WorkItemLinkKind.Blocks
                ? $"Is blocked by {project.Key}-{work.Number}." : $"Is duplicated by {project.Key}-{work.Number}.", new { request.Kind, SourceId = work.Id });
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
