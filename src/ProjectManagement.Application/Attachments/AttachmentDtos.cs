using ProjectManagement.Application.Common;
using ProjectManagement.Application.WorkItems;

namespace ProjectManagement.Application.Attachments;

public record AttachmentTarget(WorkProject Project, int? WorkItemId, int? DocumentId, string Title, bool IsArchived);
public record AttachmentInfo(int Id, long Version, string DisplayName, string ContentType, int Length,
    PersonSummary UploadedBy, DateTime UploadedAtUtc, bool CanRemove);
public record TargetAttachments(AttachmentTarget Target, int Page, int PageCount, int Total, IReadOnlyList<AttachmentInfo> Items);
public record AttachmentDetails(AttachmentTarget Target, AttachmentInfo Attachment);
public record AttachmentDownload(string DisplayName, string ContentType, byte[] Contents);
