namespace ProjectManagement.Domain.Entities;

public class WorkItemLabel
{
    public int ProjectId { get; set; }
    public int WorkItemId { get; set; }
    public int LabelId { get; set; }
    public Label Label { get; set; } = null!;
}
