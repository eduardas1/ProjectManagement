using ProjectManagement.Application.Common;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Attachments;
using ProjectManagement.Application.Comments;
using ProjectManagement.Application.Documents;
using ProjectManagement.Application.Projects;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Application.WorkItems;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Infrastructure.Repositories;

public class CollaborationReadRepository(ApplicationDbContext context, ICurrentUser currentUser) : ICollaborationReadRepository
{
    private readonly IQueryable<PersonSummary> people = PersonQueries.Select(context);

    public Task<CommentWork?> GetCommentWorkAsync(int projectId, int workItemId, CancellationToken cancellationToken)
        => context.WorkItems.AsNoTracking().Where(work => work.ProjectId == projectId && work.Id == workItemId
                && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId))
            .Join(context.Projects, work => work.ProjectId, project => project.Id, (work, project) => new CommentWork(
                new WorkProject(project.Id, project.Key, project.Name, project.IsArchived, project.OwnerId == currentUser.UserId),
                work.Id, work.Number, work.Title, work.IsArchived)).SingleOrDefaultAsync(cancellationToken);

    private IQueryable<Comment> CommentQuery(int projectId, int workItemId)
        => context.Comments.AsNoTracking().Where(comment => comment.ProjectId == projectId && comment.WorkItemId == workItemId
            && context.WorkItems.Any(work => work.ProjectId == projectId && work.Id == workItemId)
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId));

    private IQueryable<CommentItem> ProjectComments(IQueryable<Comment> query)
        => query.Select(comment => new CommentItem(comment.Id, comment.IsDeleted ? "" : comment.Body, comment.Version,
            new PersonSummary(comment.AuthorId,
                people.Where(user => user.Id == comment.AuthorId).Select(user => user.FirstName).Single(),
                people.Where(user => user.Id == comment.AuthorId).Select(user => user.LastName).Single(),
                people.Where(user => user.Id == comment.AuthorId).Select(user => user.Email).Single()),
            comment.CreatedAtUtc, comment.UpdatedAtUtc, comment.IsDeleted,
            (comment.DeletedById == null ? null : new PersonSummary(comment.DeletedById,
                people.Where(user => user.Id == comment.DeletedById).Select(user => user.FirstName).FirstOrDefault() ?? "",
                people.Where(user => user.Id == comment.DeletedById).Select(user => user.LastName).FirstOrDefault() ?? "",
                people.Where(user => user.Id == comment.DeletedById).Select(user => user.Email).FirstOrDefault() ?? "")),
            comment.DeletedAtUtc, comment.ModerationReason, comment.AuthorId == currentUser.UserId,
            !comment.IsDeleted && (comment.AuthorId == currentUser.UserId
                || context.Projects.Any(project => project.Id == comment.ProjectId && project.OwnerId == currentUser.UserId))));

    public async Task<WorkComments?> ListCommentsAsync(int projectId, int workItemId, int page, CancellationToken cancellationToken)
    {
        var work = await GetCommentWorkAsync(projectId, workItemId, cancellationToken);
        if (work == null) return null;
        var query = CommentQuery(projectId, workItemId);
        var total = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (total + 19) / 20);
        page = Math.Clamp(page, 1, pages);
        var items = await ProjectComments(query.OrderByDescending(comment => comment.CreatedAtUtc).ThenByDescending(comment => comment.Id)
            .Skip((page - 1) * 20).Take(20)).ToListAsync(cancellationToken);
        return new WorkComments(work, page, pages, total, items);
    }

    public async Task<WorkComment?> GetCommentAsync(int projectId, int workItemId, int commentId, CancellationToken cancellationToken)
    {
        var work = await GetCommentWorkAsync(projectId, workItemId, cancellationToken);
        if (work == null) return null;
        var comment = await ProjectComments(CommentQuery(projectId, workItemId).Where(comment => comment.Id == commentId))
            .SingleOrDefaultAsync(cancellationToken);
        return comment == null ? null : new WorkComment(work, comment);
    }

    public Task<WorkProject?> GetDocumentProjectAsync(int projectId, CancellationToken cancellationToken)
        => context.Projects.AsNoTracking().Where(project => project.Id == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId))
            .Select(project => new WorkProject(project.Id, project.Key, project.Name, project.IsArchived,
                project.OwnerId == currentUser.UserId)).SingleOrDefaultAsync(cancellationToken);

    private IQueryable<ProjectDocument> DocumentQuery(int projectId)
        => context.ProjectDocuments.AsNoTracking().Where(document => document.ProjectId == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId));

    private IQueryable<DocumentInfo> SelectDocumentInfo(IQueryable<ProjectDocument> query)
        => query.Select(document => new DocumentInfo(document.Id, document.Title, document.Version, document.IsArchived,
            new PersonSummary(document.CreatedById,
                people.Where(user => user.Id == document.CreatedById).Select(user => user.FirstName).Single(),
                people.Where(user => user.Id == document.CreatedById).Select(user => user.LastName).Single(),
                people.Where(user => user.Id == document.CreatedById).Select(user => user.Email).Single()),
            new PersonSummary(document.UpdatedById,
                people.Where(user => user.Id == document.UpdatedById).Select(user => user.FirstName).Single(),
                people.Where(user => user.Id == document.UpdatedById).Select(user => user.LastName).Single(),
                people.Where(user => user.Id == document.UpdatedById).Select(user => user.Email).Single()),
            document.CreatedAtUtc, document.UpdatedAtUtc));

    private IQueryable<DocumentRevision> RevisionQuery(int projectId, int documentId)
        => context.DocumentRevisions.AsNoTracking().Where(revision => revision.ProjectId == projectId && revision.DocumentId == documentId
            && context.ProjectDocuments.Any(document => document.Id == documentId && document.ProjectId == projectId)
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId));

    private IQueryable<RevisionInfo> SelectRevisionInfo(IQueryable<DocumentRevision> query)
        => query.Select(revision => new RevisionInfo(revision.Id, revision.Version, revision.Title,
            new PersonSummary(revision.EditorId,
                people.Where(user => user.Id == revision.EditorId).Select(user => user.FirstName).Single(),
                people.Where(user => user.Id == revision.EditorId).Select(user => user.LastName).Single(),
                people.Where(user => user.Id == revision.EditorId).Select(user => user.Email).Single()),
            revision.SavedAtUtc));

    public async Task<ProjectDocuments?> ListDocumentsAsync(int projectId, bool archived, int page, CancellationToken cancellationToken)
    {
        var project = await GetDocumentProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        var query = DocumentQuery(projectId).Where(document => document.IsArchived == archived);
        var total = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (total + 19) / 20);
        page = Math.Clamp(page, 1, pages);
        var items = await SelectDocumentInfo(query.OrderBy(document => document.Title).ThenBy(document => document.Id)
            .Skip((page - 1) * 20).Take(20)).ToListAsync(cancellationToken);
        return new ProjectDocuments(project, archived, page, pages, total, items);
    }

    public async Task<DocumentDetails?> GetDocumentAsync(int projectId, int documentId, CancellationToken cancellationToken)
    {
        var project = await GetDocumentProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        var query = DocumentQuery(projectId).Where(document => document.Id == documentId);
        var info = await SelectDocumentInfo(query).SingleOrDefaultAsync(cancellationToken);
        if (info == null) return null;
        var body = await query.Select(document => document.Body).SingleOrDefaultAsync(cancellationToken);
        if (body == null) return null;
        var history = await context.ChangeEvents.AsNoTracking().Where(change => change.ProjectId == projectId && change.DocumentId == documentId
                && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId))
            .OrderByDescending(change => change.OccurredAtUtc).ThenByDescending(change => change.Id).Take(50)
            .Select(change => new ProjectHistory(
                new PersonSummary(change.ActorId,
                people.Where(user => user.Id == change.ActorId).Select(user => user.FirstName).Single(),
                people.Where(user => user.Id == change.ActorId).Select(user => user.LastName).Single(),
                people.Where(user => user.Id == change.ActorId).Select(user => user.Email).Single()),
                change.OccurredAtUtc, change.Summary)).ToListAsync(cancellationToken);
        return new DocumentDetails(project, info, body, history);
    }

    public async Task<DocumentRevisions?> ListRevisionsAsync(int projectId, int documentId, int page, CancellationToken cancellationToken)
    {
        var project = await GetDocumentProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        var document = await SelectDocumentInfo(DocumentQuery(projectId).Where(document => document.Id == documentId))
            .SingleOrDefaultAsync(cancellationToken);
        if (document == null) return null;
        var query = RevisionQuery(projectId, documentId);
        var total = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (total + 19) / 20);
        page = Math.Clamp(page, 1, pages);
        var items = await SelectRevisionInfo(query.OrderByDescending(revision => revision.Version)
            .Skip((page - 1) * 20).Take(20)).ToListAsync(cancellationToken);
        return new DocumentRevisions(project, document, page, pages, total, items);
    }

    public async Task<DocumentRevisionDetails?> GetRevisionAsync(int projectId, int documentId, int revisionId, CancellationToken cancellationToken)
    {
        var project = await GetDocumentProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        var document = await SelectDocumentInfo(DocumentQuery(projectId).Where(document => document.Id == documentId))
            .SingleOrDefaultAsync(cancellationToken);
        if (document == null) return null;
        var query = RevisionQuery(projectId, documentId).Where(revision => revision.Id == revisionId);
        var info = await SelectRevisionInfo(query).SingleOrDefaultAsync(cancellationToken);
        if (info == null) return null;
        var body = await query.Select(revision => revision.Body).SingleOrDefaultAsync(cancellationToken);
        return body == null ? null : new DocumentRevisionDetails(project, document, info, body);
    }

    public async Task<AttachmentTarget?> GetAttachmentTargetAsync(int projectId, int? workItemId, int? documentId, CancellationToken cancellationToken)
    {
        if (workItemId.HasValue == documentId.HasValue) return null;
        var project = await GetDocumentProjectAsync(projectId, cancellationToken);
        if (project == null) return null;
        if (workItemId.HasValue)
        {
            var work = await context.WorkItems.AsNoTracking().Where(work => work.ProjectId == projectId && work.Id == workItemId.Value
                    && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId))
                .Select(work => new { work.Id, work.Number, work.Title, work.IsArchived }).SingleOrDefaultAsync(cancellationToken);
            return work == null ? null : new AttachmentTarget(project, work.Id, null, project.Key + "-" + work.Number + " " + work.Title, work.IsArchived);
        }
        var document = await DocumentQuery(projectId).Where(document => document.Id == documentId!.Value)
            .Select(document => new { document.Id, document.Title, document.IsArchived }).SingleOrDefaultAsync(cancellationToken);
        return document == null ? null : new AttachmentTarget(project, null, document.Id, document.Title, document.IsArchived);
    }

    private IQueryable<Attachment> AttachmentQuery(int projectId)
        => context.Attachments.AsNoTracking().Where(attachment => attachment.ProjectId == projectId
            && context.ProjectMembers.Any(member => member.ProjectId == projectId && member.UserId == currentUser.UserId)
            && ((attachment.WorkItemId != null && context.WorkItems.Any(work => work.ProjectId == projectId && work.Id == attachment.WorkItemId))
                || (attachment.DocumentId != null && context.ProjectDocuments.Any(document => document.ProjectId == projectId && document.Id == attachment.DocumentId))));

    private IQueryable<AttachmentInfo> SelectAttachmentInfo(IQueryable<Attachment> query)
        => query.Select(attachment => new AttachmentInfo(attachment.Id, attachment.Version, attachment.DisplayName,
            attachment.ContentType, attachment.Length,
            new PersonSummary(attachment.UploadedById,
                people.Where(user => user.Id == attachment.UploadedById).Select(user => user.FirstName).Single(),
                people.Where(user => user.Id == attachment.UploadedById).Select(user => user.LastName).Single(),
                people.Where(user => user.Id == attachment.UploadedById).Select(user => user.Email).Single()),
            attachment.UploadedAtUtc, attachment.UploadedById == currentUser.UserId
                || context.Projects.Any(project => project.Id == attachment.ProjectId && project.OwnerId == currentUser.UserId)));

    public async Task<TargetAttachments?> ListAttachmentsAsync(int projectId, int? workItemId, int? documentId, int page, CancellationToken cancellationToken)
    {
        var target = await GetAttachmentTargetAsync(projectId, workItemId, documentId, cancellationToken);
        if (target == null) return null;
        var query = AttachmentQuery(projectId).Where(attachment => attachment.WorkItemId == workItemId && attachment.DocumentId == documentId);
        var total = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (total + 19) / 20);
        page = Math.Clamp(page, 1, pages);
        var items = await SelectAttachmentInfo(query.OrderByDescending(attachment => attachment.UploadedAtUtc).ThenByDescending(attachment => attachment.Id)
            .Skip((page - 1) * 20).Take(20)).ToListAsync(cancellationToken);
        return new TargetAttachments(target, page, pages, total, items);
    }

    public async Task<AttachmentDetails?> GetAttachmentAsync(int projectId, int attachmentId, CancellationToken cancellationToken)
    {
        var query = AttachmentQuery(projectId).Where(attachment => attachment.Id == attachmentId);
        var parent = await query.Select(attachment => new { attachment.WorkItemId, attachment.DocumentId }).SingleOrDefaultAsync(cancellationToken);
        if (parent == null) return null;
        var target = await GetAttachmentTargetAsync(projectId, parent.WorkItemId, parent.DocumentId, cancellationToken);
        if (target == null) return null;
        var info = await SelectAttachmentInfo(query).SingleOrDefaultAsync(cancellationToken);
        return info == null ? null : new AttachmentDetails(target, info);
    }

    public Task<AttachmentDownload?> DownloadAttachmentAsync(int projectId, int attachmentId, CancellationToken cancellationToken)
        => AttachmentQuery(projectId).Where(attachment => attachment.Id == attachmentId)
            .Select(attachment => new AttachmentDownload(attachment.DisplayName, attachment.ContentType, attachment.Contents))
            .SingleOrDefaultAsync(cancellationToken);
}
