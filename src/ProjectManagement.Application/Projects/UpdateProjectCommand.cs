using System.Text.Json;
using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Application.Projects;

public record UpdateProjectCommand(int ProjectId, long ExpectedVersion, string Name, string Description) : IRequest<OperationResult<int>>;

public class UpdateProjectHandler(IProjectRepository repository, IProjectAccess access,
    IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<UpdateProjectCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(UpdateProjectCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetOwnerProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            if (project.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before editing it.");
            if (!ProjectValidation.IsValidName(request.Name))
                return OperationResult<int>.Invalid("Name", "Enter a project name of up to 100 characters.");
            if (!ProjectValidation.IsValidDescription(request.Description))
                return OperationResult<int>.Invalid("Description", "The description must not exceed 20,000 characters.");
            var name = request.Name.Trim();
            if (project.Name == name && project.Description == request.Description)
                return OperationResult<int>.Success(project.Id);

            var previous = new { project.Name, project.Description };
            project.Name = name;
            project.Description = request.Description;
            project.Version++;
            project.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            repository.AddEvent(new ChangeEvent
            {
                ProjectId = project.Id, ActorId = currentUser.UserId, OccurredAtUtc = project.UpdatedAtUtc,
                Action = "ProjectUpdated", Summary = "Project details updated.",
                DetailsJson = JsonSerializer.Serialize(new { Previous = previous, Current = new { project.Name, project.Description } })
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
