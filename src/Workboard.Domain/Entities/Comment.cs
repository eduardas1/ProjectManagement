namespace Workboard.Domain.Entities;

public class Comment
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int WorkItemId { get; set; }
    public string AuthorId { get; set; } = "";
    public string Body { get; set; } = "";
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public string? DeletedById { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? ModerationReason { get; set; }
}
