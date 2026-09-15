using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Application.WorkItems;

public class WorkItemValidation(IWorkItemRepository repository, IProjectRepository projects, ILabelRepository labels)
{
    public async Task<OperationResult<int>?> ValidateFieldsAsync(int projectId, string title, string description,
        WorkItemPriority priority, string? assigneeId, IReadOnlyList<int> labelIds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            return OperationResult<int>.Invalid("Title", "Enter a title of up to 200 characters.");
        if (description == null || description.Length > 20000)
            return OperationResult<int>.Invalid("Description", "The description must not exceed 20,000 characters.");
        if (!Enum.IsDefined(priority)) return OperationResult<int>.Invalid("Priority", "Select a valid priority.");
        if (assigneeId != null && await projects.FindMemberAsync(projectId, assigneeId, cancellationToken) == null)
            return OperationResult<int>.Invalid("AssigneeId", "Select a current project member.");
        if (labelIds.Any(id => id <= 0) || !await labels.AllExistAsync(projectId, labelIds, cancellationToken))
            return OperationResult<int>.Invalid("LabelIds", "Select labels from this project.");
        return null;
    }

    public async Task<OperationResult<int>?> ValidateHierarchyAsync(WorkItem work, int? parentId, int? epicId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(work.Type)) return OperationResult<int>.Invalid("Type", "Select a valid work type.");
        if (parentId == work.Id || epicId == work.Id)
            return OperationResult<int>.Invalid("", "An item cannot be its own parent or Epic.");
        if (work.Type == WorkItemType.Epic)
            return parentId == null && epicId == null ? null : OperationResult<int>.Invalid("", "An Epic cannot have a parent or another Epic.");
        if (work.Type == WorkItemType.Subtask)
        {
            if (parentId == null || epicId != null)
                return OperationResult<int>.Invalid("ParentId", "A Subtask needs an ordinary parent and inherits its Epic.");
            var parent = await repository.FindAsync(work.ProjectId, parentId.Value, cancellationToken);
            if (parent == null || parent.IsArchived || parent.Type is WorkItemType.Epic or WorkItemType.Subtask)
                return OperationResult<int>.Invalid("ParentId", "Select an active Bug, Task or Story from this project.");
            if (parent.Status == WorkItemStatus.Done && !WorkItemChanges.IsClosed(work.Status))
                return OperationResult<int>.Invalid("ParentId", "Reopen the completed parent before adding unfinished work.");
            epicId = parent.EpicId;
        }
        else if (parentId != null)
            return OperationResult<int>.Invalid("ParentId", "Only a Subtask has a direct parent.");

        if (epicId == null) return null;
        var epic = await repository.FindAsync(work.ProjectId, epicId.Value, cancellationToken);
        if (epic == null || epic.IsArchived || epic.Type != WorkItemType.Epic)
            return OperationResult<int>.Invalid("EpicId", "Select an active Epic from this project.");
        if (epic.Status == WorkItemStatus.Done)
        {
            var descendants = work.Id == 0 ? Array.Empty<WorkItem>()
                : await repository.GetActiveDescendantsAsync(work.ProjectId, work.Id, cancellationToken);
            if (!WorkItemChanges.IsClosed(work.Status) || descendants.Any(item => !WorkItemChanges.IsClosed(item.Status)))
                return OperationResult<int>.Invalid("EpicId", "Reopen the completed Epic before adding unfinished work.");
        }
        return null;
    }
}
