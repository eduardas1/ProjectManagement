using Workboard.Domain.Entities;

namespace Workboard.Application.Interfaces;

public interface ICollaborationRepository
{
    Task<Comment?> FindCommentAsync(int projectId, int workItemId, int commentId, CancellationToken cancellationToken);
    void AddComment(Comment comment);
    Task<ProjectDocument?> FindDocumentAsync(int projectId, int documentId, CancellationToken cancellationToken);
    void AddDocument(ProjectDocument document);
    void AddRevision(DocumentRevision revision);
}
