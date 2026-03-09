namespace Workboard.Domain.Entities;

public class DocumentRevision
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int DocumentId { get; set; }
    public long Version { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string EditorId { get; set; } = "";
    public DateTime SavedAtUtc { get; set; }
}
