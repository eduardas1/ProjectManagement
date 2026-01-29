using Workboard.Domain.Enums;

namespace Workboard.Domain.Entities;

public class WorkItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int Number { get; set; }
    public WorkItemType Type { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Backlog;
    public WorkItemPriority Priority { get; set; } = WorkItemPriority.Medium;
    public string? AssigneeId { get; set; }
    public int? ParentId { get; set; }
    public int? EpicId { get; set; }
    public DateOnly? DueDate { get; set; }
    public string CreatedById { get; set; } = "";
    public bool IsArchived { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
