using System.Text.Json;
using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Application.WorkItems;

namespace ProjectManagement.Application.Projects;

public record RemoveProjectMemberCommand(int ProjectId, long ExpectedVersion, string UserId) : IRequest<OperationResult<int>>;

public class RemoveProjectMemberHandler(IProjectRepository repository, IProjectAccess access,
    IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider, IWorkItemRepository workItems)
    : IRequestHandler<RemoveProjectMemberCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(RemoveProjectMemberCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetOwnerProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            if (project.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.OwnerId == request.UserId) return OperationResult<int>.Invalid("", "Transfer ownership before removing this member.");
            var member = await repository.FindMemberAsync(project.Id, request.UserId, cancellationToken);
            if (member == null) return OperationResult<int>.Invalid("", "This account is not a project member.");

            var assigned = await workItems.GetActiveAssignedAsync(project.Id, request.UserId, cancellationToken);
            if (project.Version == long.MaxValue || assigned.Any(work => work.Version == long.MaxValue))
                return OperationResult<int>.Invalid("", "A project or work item version limit has been reached.");
            var now = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var work in assigned)
            {
                work.AssigneeId = null;
                WorkItemChanges.Touch(work, now);
                WorkItemChanges.Record(repository, work, currentUser.UserId, now, "AssigneeCleared",
                    "Assignment cleared after membership revocation.", new { PreviousAssigneeId = request.UserId });
            }
            repository.RemoveMember(member);
            project.Version++;
            project.UpdatedAtUtc = now;
            repository.AddEvent(new ChangeEvent
            {
                ProjectId = project.Id, ActorId = currentUser.UserId, OccurredAtUtc = project.UpdatedAtUtc,
                Action = "MemberRemoved", Summary = "Project membership revoked.", DetailsJson = JsonSerializer.Serialize(new { request.UserId })
            });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<int>.Success(project.Id);
        }
        catch (WriteException exception)
        {
            return exception.Failure == WriteFailure.Busy ? OperationResult<int>.Busy() : OperationResult<int>.Conflict();
        }
    }
}
