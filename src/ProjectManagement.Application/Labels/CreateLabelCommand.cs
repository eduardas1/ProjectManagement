using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Application.Labels;

public record CreateLabelCommand(int ProjectId, string Name) : IRequest<OperationResult<int>>;

public class CreateLabelHandler(ILabelRepository repository, IProjectRepository projects, IProjectAccess access,
    IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider) : IRequestHandler<CreateLabelCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(CreateLabelCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before adding labels.");
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 40)
                return OperationResult<int>.Invalid("Name", "Enter a label name of up to 40 characters.");
            var name = request.Name.Trim();
            var normalized = name.ToUpperInvariant();
            if (await repository.NameExistsAsync(project.Id, normalized, null, cancellationToken))
                return OperationResult<int>.Invalid("Name", "This project already has that label.");
            var label = new Label { ProjectId = project.Id, Name = name, NormalizedName = normalized };
            repository.Add(label);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            projects.AddEvent(new ChangeEvent { ProjectId = project.Id, ActorId = currentUser.UserId,
                OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime, Action = "LabelCreated", Summary = "Project label created.",
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { LabelId = label.Id, label.Name }) });
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
