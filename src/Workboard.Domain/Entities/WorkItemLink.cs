using Workboard.Domain.Enums;

namespace Workboard.Domain.Entities;

public class WorkItemLink
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int SourceId { get; set; }
    public int TargetId { get; set; }
    public WorkItemLinkKind Kind { get; set; }
    public string CreatedById { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
