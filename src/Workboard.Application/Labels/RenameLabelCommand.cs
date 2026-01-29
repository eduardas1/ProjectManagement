using System.Text.Json;
using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;

namespace Workboard.Application.Labels;

public record RenameLabelCommand(int ProjectId, int LabelId, long ExpectedVersion, string Name) : IRequest<OperationResult<int>>;

public class RenameLabelHandler(ILabelRepository repository, IProjectRepository projects, IProjectAccess access,
    IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider) : IRequestHandler<RenameLabelCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(RenameLabelCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var label = await repository.FindAsync(project.Id, request.LabelId, cancellationToken);
            if (label == null) return OperationResult<int>.NotFound();
            if (label.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before editing labels.");
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 40)
                return OperationResult<int>.Invalid("Name", "Enter a label name of up to 40 characters.");
            var name = request.Name.Trim();
            var normalized = name.ToUpperInvariant();
            if (await repository.NameExistsAsync(project.Id, normalized, label.Id, cancellationToken))
                return OperationResult<int>.Invalid("Name", "This project already has that label.");
            if (label.Name == name) return OperationResult<int>.Success(label.Id);
            if (label.Version == long.MaxValue) return OperationResult<int>.Invalid("", "This label's version limit has been reached.");
            var previous = label.Name;
            label.Name = name; label.NormalizedName = normalized; label.Version++;
            projects.AddEvent(new ChangeEvent { ProjectId = project.Id, ActorId = currentUser.UserId,
                OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime, Action = "LabelRenamed", Summary = "Project label renamed.",
                DetailsJson = JsonSerializer.Serialize(new { LabelId = label.Id, Previous = previous, Current = name }) });
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
