using MediatR;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Application.WorkItems;

public record UpdateWorkItemCommand(int ProjectId, int WorkItemId, long ExpectedVersion, string Title,
    string Description, WorkItemPriority Priority, string? AssigneeId, DateOnly? DueDate,
    IReadOnlyList<int> LabelIds) : IRequest<OperationResult<int>>;

public class UpdateWorkItemHandler(IWorkItemRepository repository, IProjectRepository projects,
    IProjectAccess access, IUnitOfWork unitOfWork, WorkItemValidation validation, ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<UpdateWorkItemCommand, OperationResult<int>>
{
    public async Task<OperationResult<int>> Handle(UpdateWorkItemCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginWriteAsync(cancellationToken);
            var project = await access.GetMemberProjectAsync(request.ProjectId, cancellationToken);
            if (project == null) return OperationResult<int>.NotFound();
            var work = await repository.FindAsync(project.Id, request.WorkItemId, cancellationToken);
            if (work == null) return OperationResult<int>.NotFound();
            if (work.Version != request.ExpectedVersion) return OperationResult<int>.Conflict();
            if (project.IsArchived || work.IsArchived) return OperationResult<int>.Invalid("", "Restore this project and item before editing.");
            var assigneeId = string.IsNullOrWhiteSpace(request.AssigneeId) ? null : request.AssigneeId;
            var error = await validation.ValidateFieldsAsync(project.Id, request.Title, request.Description,
                request.Priority, assigneeId, request.LabelIds, cancellationToken);
            if (error != null) return error;
            var title = request.Title.Trim();
            var currentLabels = await repository.GetLabelsAsync(project.Id, work.Id, cancellationToken);
            var labelIds = request.LabelIds.Distinct().ToHashSet();
            var changed = new List<string>();
            if (work.Title != title) changed.Add("Title");
            if (work.Description != request.Description) changed.Add("Description");
            if (work.Priority != request.Priority) changed.Add("Priority");
            if (work.AssigneeId != assigneeId) changed.Add("AssigneeId");
            if (work.DueDate != request.DueDate) changed.Add("DueDate");
            if (!labelIds.SetEquals(currentLabels.Select(label => label.LabelId))) changed.Add("Labels");
            if (changed.Count == 0) return OperationResult<int>.Success(work.Id);
            if (work.Version == long.MaxValue) return OperationResult<int>.Invalid("", "This item's version limit has been reached.");
            var previous = new { work.Title, work.Priority, work.AssigneeId, work.DueDate, LabelIds = currentLabels.Select(label => label.LabelId).Order().ToArray() };
            work.Title = title; work.Description = request.Description; work.Priority = request.Priority;
            work.AssigneeId = assigneeId; work.DueDate = request.DueDate;
            foreach (var label in currentLabels.Where(label => !labelIds.Contains(label.LabelId))) repository.RemoveLabel(label);
            foreach (var labelId in labelIds.Except(currentLabels.Select(label => label.LabelId)))
                repository.AddLabel(new WorkItemLabel { ProjectId = project.Id, WorkItemId = work.Id, LabelId = labelId });
            var now = timeProvider.GetUtcNow().UtcDateTime;
            WorkItemChanges.Touch(work, now);
            WorkItemChanges.Record(projects, work, currentUser.UserId, now, "WorkUpdated", "Updated " + string.Join(", ", changed) + ".",
                new { Fields = changed, Previous = previous, Current = new { work.Title, work.Priority, work.AssigneeId, work.DueDate, LabelIds = labelIds.Order().ToArray() } });
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
