using System.Text.Json;
using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;

namespace Workboard.Application.Projects;

public record SetProjectArchiveCommand(int ProjectId, long ExpectedVersion, bool IsArchived) : IRequest<OperationResult<int>>;

public class SetProjectArchiveHandler(IProjectRepository repository, IProjectAccess access,
    IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<SetProjectArchiveCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(SetProjectArchiveCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetOwnerProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            if (project.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived == request.IsArchived) return OperationResult<int>.Success(project.Id);

            project.IsArchived = request.IsArchived;
            project.Version++;
            project.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            repository.AddEvent(new ChangeEvent
            {
                ProjectId = project.Id, ActorId = currentUser.UserId, OccurredAtUtc = project.UpdatedAtUtc,
                Action = request.IsArchived ? "ProjectArchived" : "ProjectRestored",
                Summary = request.IsArchived ? "Project archived." : "Project restored.",
                DetailsJson = JsonSerializer.Serialize(new { project.IsArchived })
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
