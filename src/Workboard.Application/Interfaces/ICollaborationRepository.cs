using Workboard.Domain.Entities;

namespace Workboard.Application.Interfaces;

public interface ICollaborationRepository
{
    Task<Comment?> FindCommentAsync(int projectId, int workItemId, int commentId, CancellationToken cancellationToken);
    void AddComment(Comment comment);
    Task<ProjectDocument?> FindDocumentAsync(int projectId, int documentId, CancellationToken cancellationToken);
    void AddDocument(ProjectDocument document);
    void AddRevision(DocumentRevision revision);
    Task<Attachment?> FindAttachmentAsync(int projectId, int attachmentId, CancellationToken cancellationToken);
    void AddAttachment(Attachment attachment);
    void RemoveAttachment(Attachment attachment);
}
