using System.Text.Json;
using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;

namespace Workboard.Application.Projects;

public record AddProjectMemberCommand(int ProjectId, long ExpectedVersion, string Email) : IRequest<OperationResult<int>>;

public class AddProjectMemberHandler(IProjectRepository repository, IProjectAccess access,
    IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<AddProjectMemberCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(AddProjectMemberCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetOwnerProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            if (project.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before adding a member.");
            if (request.Email.Length > 254 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email.Trim()))
                return OperationResult<int>.Invalid("Email", "Enter a valid account email address.");
            var userId = await repository.FindAccountIdAsync(request.Email, cancellationToken);
            if (userId == null) return OperationResult<int>.Invalid("Email", "No account was found for this email address.");
            if (await repository.FindMemberAsync(project.Id, userId, cancellationToken) != null)
                return OperationResult<int>.Invalid("Email", "This account is already a project member.");

            var now = timeProvider.GetUtcNow().UtcDateTime;
            repository.AddMember(new ProjectMember { ProjectId = project.Id, UserId = userId, JoinedAtUtc = now });
            project.Version++;
            project.UpdatedAtUtc = now;
            repository.AddEvent(new ChangeEvent
            {
                ProjectId = project.Id, ActorId = currentUser.UserId, OccurredAtUtc = now,
                Action = "MemberAdded", Summary = "Project member added.", DetailsJson = JsonSerializer.Serialize(new { UserId = userId })
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
