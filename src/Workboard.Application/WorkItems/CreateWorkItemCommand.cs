using MediatR;
using Workboard.Application.Common;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;
using Workboard.Domain.Enums;

namespace Workboard.Application.WorkItems;

public record CreateWorkItemCommand(int ProjectId, WorkItemType Type, string Title, string Description,
    WorkItemPriority Priority, string? AssigneeId, DateOnly? DueDate, int? ParentId, int? EpicId,
    IReadOnlyList<int> LabelIds) : IRequest<OperationResult<int>>;

public class CreateWorkItemHandler(IWorkItemRepository repository, IProjectRepository projects,
    IProjectAccess access, IUnitOfWork unitOfWork, WorkItemValidation validation, ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<CreateWorkItemCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(CreateWorkItemCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            if (project.IsArchived) return OperationResult<int>.Invalid("", "Restore this project before adding work.");
            if (project.NextWorkItemNumber == int.MaxValue || project.Version == long.MaxValue)
                return OperationResult<int>.Invalid("", "This project's work number or version limit has been reached.");
            var assigneeId = string.IsNullOrWhiteSpace(request.AssigneeId) ? null : request.AssigneeId;
            var error = await validation.ValidateFieldsAsync(project.Id, request.Title, request.Description,
                request.Priority, assigneeId, request.LabelIds, cancellationToken);
            if (error != null) return error;
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var work = new WorkItem
            {
                ProjectId = project.Id, Number = project.NextWorkItemNumber, Type = request.Type,
                Title = request.Title.Trim(), Description = request.Description, Priority = request.Priority,
                AssigneeId = assigneeId, DueDate = request.DueDate, ParentId = request.ParentId, EpicId = request.EpicId,
                CreatedById = currentUser.UserId, CreatedAtUtc = now, UpdatedAtUtc = now
            };
            error = await validation.ValidateHierarchyAsync(work, request.ParentId, request.EpicId, cancellationToken);
            if (error != null) return error;
            project.NextWorkItemNumber++;
            project.Version++;
            project.UpdatedAtUtc = now;
            repository.Add(work);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            foreach (var labelId in request.LabelIds.Distinct())
                repository.AddLabel(new WorkItemLabel { ProjectId = project.Id, WorkItemId = work.Id, LabelId = labelId });
            WorkItemChanges.Record(projects, work, currentUser.UserId, now, "WorkCreated", "Work item created.",
                new { work.Number, work.Type, work.Priority, work.AssigneeId, work.DueDate, work.ParentId, work.EpicId, LabelIds = request.LabelIds.Distinct().Order().ToArray() });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<int>.Success(work.Id);
        }
        catch (WriteException exception)
        {
            return exception.Failure == WriteFailure.Busy ? OperationResult<int>.Busy() : OperationResult<int>.Conflict();
        }
    }
}
