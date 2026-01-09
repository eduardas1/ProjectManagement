using System.Text.Json;
using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;

namespace Workboard.Application.Projects;

public record RemoveProjectMemberCommand(int ProjectId, long ExpectedVersion, string UserId) : IRequest<OperationResult<int>>;

public class RemoveProjectMemberHandler(IProjectRepository repository, IProjectAccess access,
    IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider)
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

            repository.RemoveMember(member);
            project.Version++;
            project.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
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
