namespace Workboard.Domain.Entities;

public class ChangeEvent
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? WorkItemId { get; set; }
    public string ActorId { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public string Action { get; set; } = "";
    public string Summary { get; set; } = "";
    public string DetailsJson { get; set; } = "{}";
}
