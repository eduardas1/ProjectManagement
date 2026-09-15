namespace ProjectManagement.Domain.Entities;

public class Project
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public int NextWorkItemNumber { get; set; } = 1;
    public bool IsArchived { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
