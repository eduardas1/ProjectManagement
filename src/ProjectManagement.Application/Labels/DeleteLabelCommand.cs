using System.Text.Json;
using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Application.Labels;

public record DeleteLabelCommand(int ProjectId, int LabelId, long ExpectedVersion) : IRequest<OperationResult<int>>;

public class DeleteLabelHandler(ILabelRepository repository, IProjectRepository projects, IProjectAccess access,
    IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider) : IRequestHandler<DeleteLabelCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(DeleteLabelCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetOwnerProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var label = await repository.FindAsync(project.Id, request.LabelId, cancellationToken);
            if (label == null) return OperationResult<int>.NotFound();
            if (label.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before deleting labels.");
            if (await repository.IsInUseAsync(project.Id, label.Id, cancellationToken))
                return OperationResult<int>.Invalid("", "Remove this label from all work items before deleting it.");
            repository.Remove(label);
            projects.AddEvent(new ChangeEvent { ProjectId = project.Id, ActorId = currentUser.UserId,
                OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime, Action = "LabelDeleted", Summary = "Unused project label deleted.",
                DetailsJson = JsonSerializer.Serialize(new { LabelId = label.Id, label.Name }) });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<int>.Success(label.Id);
        }
        catch (WriteException exception)
        {
            return exception.Failure == WriteFailure.Busy ? OperationResult<int>.Busy() : OperationResult<int>.Conflict();
        }
    }
}
