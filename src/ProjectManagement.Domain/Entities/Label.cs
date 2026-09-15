namespace ProjectManagement.Domain.Entities;

public class Label
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public long Version { get; set; } = 1;
}
