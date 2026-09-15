using System.Text.Json;
using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Application.Projects;

public record TransferProjectOwnershipCommand(int ProjectId, long ExpectedVersion, string UserId) : IRequest<OperationResult<int>>;

public class TransferProjectOwnershipHandler(IProjectRepository repository, IProjectAccess access,
    IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<TransferProjectOwnershipCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(TransferProjectOwnershipCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetOwnerProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            if (project.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.OwnerId == request.UserId) return OperationResult<int>.Success(project.Id);
            if (await repository.FindMemberAsync(project.Id, request.UserId, cancellationToken) == null)
                return OperationResult<int>.Invalid("UserId", "Select a current project member.");

            var previousOwnerId = project.OwnerId;
            project.OwnerId = request.UserId;
            project.Version++;
            project.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            repository.AddEvent(new ChangeEvent
            {
                ProjectId = project.Id, ActorId = currentUser.UserId, OccurredAtUtc = project.UpdatedAtUtc,
                Action = "OwnershipTransferred", Summary = "Project ownership transferred.",
                DetailsJson = JsonSerializer.Serialize(new { PreviousOwnerId = previousOwnerId, OwnerId = project.OwnerId })
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
