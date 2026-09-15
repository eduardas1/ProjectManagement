using ProjectManagement.Application.Attachments;
using ProjectManagement.Application.Comments;
using ProjectManagement.Application.Documents;
using ProjectManagement.Application.WorkItems;

namespace ProjectManagement.Application.Interfaces;

public interface ICollaborationReadRepository
{
    Task<AttachmentTarget?> GetAttachmentTargetAsync(int projectId, int? workItemId, int? documentId, CancellationToken cancellationToken);
    Task<TargetAttachments?> ListAttachmentsAsync(int projectId, int? workItemId, int? documentId, int page, CancellationToken cancellationToken);
    Task<AttachmentDetails?> GetAttachmentAsync(int projectId, int attachmentId, CancellationToken cancellationToken);
    Task<AttachmentDownload?> DownloadAttachmentAsync(int projectId, int attachmentId, CancellationToken cancellationToken);
    Task<WorkProject?> GetDocumentProjectAsync(int projectId, CancellationToken cancellationToken);
    Task<ProjectDocuments?> ListDocumentsAsync(int projectId, bool archived, int page, CancellationToken cancellationToken);
    Task<DocumentDetails?> GetDocumentAsync(int projectId, int documentId, CancellationToken cancellationToken);
    Task<DocumentRevisions?> ListRevisionsAsync(int projectId, int documentId, int page, CancellationToken cancellationToken);
    Task<DocumentRevisionDetails?> GetRevisionAsync(int projectId, int documentId, int revisionId, CancellationToken cancellationToken);
    Task<CommentWork?> GetCommentWorkAsync(int projectId, int workItemId, CancellationToken cancellationToken);
    Task<WorkComments?> ListCommentsAsync(int projectId, int workItemId, int page, CancellationToken cancellationToken);
    Task<WorkComment?> GetCommentAsync(int projectId, int workItemId, int commentId, CancellationToken cancellationToken);
}
