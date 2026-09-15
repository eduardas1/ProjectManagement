namespace ProjectManagement.Domain.Entities;

public class SavedFilter
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string Expression { get; set; } = "";
    public string Ordering { get; set; } = "number";
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
