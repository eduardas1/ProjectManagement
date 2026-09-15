using System.Text.Json;
using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Application.Projects;

public record CreateProjectCommand(string Key, string Name, string Description) : IRequest<OperationResult<int>>;

public class CreateProjectHandler(IProjectRepository repository, IUnitOfWork unitOfWork,
    ICurrentUser currentUser, TimeProvider timeProvider) : IRequestHandler<CreateProjectCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        var key = ProjectValidation.NormalizeKey(request.Key);
        if (!ProjectValidation.IsValidKey(key))
            return OperationResult<int>.Invalid("Key", "Use 2 to 10 letters or digits, starting with a letter.");
        if (!ProjectValidation.IsValidName(request.Name))
            return OperationResult<int>.Invalid("Name", "Enter a project name of up to 100 characters.");
        if (!ProjectValidation.IsValidDescription(request.Description))
            return OperationResult<int>.Invalid("Description", "The description must not exceed 20,000 characters.");

        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            if (await repository.KeyExistsAsync(key, cancellationToken))
                return OperationResult<int>.Invalid("Key", "This project key is already in use.");

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var project = new Project
            {
                Key = key, Name = request.Name.Trim(), Description = request.Description,
                OwnerId = currentUser.UserId, CreatedAtUtc = now, UpdatedAtUtc = now
            };
            repository.Add(project);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            repository.AddMember(new ProjectMember { ProjectId = project.Id, UserId = currentUser.UserId, JoinedAtUtc = now });
            repository.AddEvent(new ChangeEvent
            {
                ProjectId = project.Id, ActorId = currentUser.UserId, OccurredAtUtc = now,
                Action = "ProjectCreated", Summary = "Project created.", DetailsJson = JsonSerializer.Serialize(new { project.Key, project.Name })
            });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<int>.Success(project.Id);
        }
        catch (WriteException exception)
        {
            return exception.Failure switch
            {
                WriteFailure.DuplicateKey => OperationResult<int>.Invalid("Key", "This project key is already in use."),
                WriteFailure.Busy => OperationResult<int>.Busy(),
                _ => OperationResult<int>.Conflict()
            };
        }
    }
}
