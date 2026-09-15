namespace ProjectManagement.Domain.Entities;

public class Attachment
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? WorkItemId { get; set; }
    public int? DocumentId { get; set; }
    public string StoredName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int Length { get; set; }
    public byte[] Contents { get; set; } = [];
    public string UploadedById { get; set; } = "";
    public DateTime UploadedAtUtc { get; set; }
    public long Version { get; set; } = 1;
}
