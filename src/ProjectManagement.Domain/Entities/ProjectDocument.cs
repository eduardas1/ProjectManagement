namespace ProjectManagement.Domain.Entities;

public class ProjectDocument
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string CreatedById { get; set; } = "";
    public string UpdatedById { get; set; } = "";
    public bool IsArchived { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
