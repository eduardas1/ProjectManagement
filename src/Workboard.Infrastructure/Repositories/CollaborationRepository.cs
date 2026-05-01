using Microsoft.EntityFrameworkCore;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;
using Workboard.Infrastructure.Data;

namespace Workboard.Infrastructure.Repositories;

public class CollaborationRepository(ApplicationDbContext context) : ICollaborationRepository
{
    public Task<Comment?> FindCommentAsync(int projectId, int workItemId, int commentId, CancellationToken cancellationToken)
        => context.Comments.SingleOrDefaultAsync(comment => comment.ProjectId == projectId
            && comment.WorkItemId == workItemId && comment.Id == commentId, cancellationToken);

    public void AddComment(Comment comment) => context.Comments.Add(comment);

    public Task<ProjectDocument?> FindDocumentAsync(int projectId, int documentId, CancellationToken cancellationToken)
        => context.ProjectDocuments.SingleOrDefaultAsync(document => document.ProjectId == projectId
            && document.Id == documentId, cancellationToken);

    public void AddDocument(ProjectDocument document) => context.ProjectDocuments.Add(document);
    public void AddRevision(DocumentRevision revision) => context.DocumentRevisions.Add(revision);
}
