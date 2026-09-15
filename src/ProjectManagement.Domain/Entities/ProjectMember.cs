namespace ProjectManagement.Domain.Entities;

public class ProjectMember
{
    public int ProjectId { get; set; }
    public string UserId { get; set; } = "";
    public DateTime JoinedAtUtc { get; set; }
}
