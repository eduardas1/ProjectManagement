using Workboard.Application.Comments;
using Workboard.Application.Documents;
using Workboard.Application.WorkItems;

namespace Workboard.Application.Interfaces;

public interface ICollaborationReadRepository
{
    Task<WorkProject?> GetDocumentProjectAsync(int projectId, CancellationToken cancellationToken);
    Task<ProjectDocuments?> ListDocumentsAsync(int projectId, bool archived, int page, CancellationToken cancellationToken);
    Task<DocumentDetails?> GetDocumentAsync(int projectId, int documentId, CancellationToken cancellationToken);
    Task<DocumentRevisions?> ListRevisionsAsync(int projectId, int documentId, int page, CancellationToken cancellationToken);
    Task<DocumentRevisionDetails?> GetRevisionAsync(int projectId, int documentId, int revisionId, CancellationToken cancellationToken);
    Task<CommentWork?> GetCommentWorkAsync(int projectId, int workItemId, CancellationToken cancellationToken);
    Task<WorkComments?> ListCommentsAsync(int projectId, int workItemId, int page, CancellationToken cancellationToken);
    Task<WorkComment?> GetCommentAsync(int projectId, int workItemId, int commentId, CancellationToken cancellationToken);
}
