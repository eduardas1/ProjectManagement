using Workboard.Application.Projects;
using Workboard.Application.WorkItems;

namespace Workboard.Application.Documents;

public record DocumentInfo(int Id, string Title, long Version, bool IsArchived, string CreatedBy, string UpdatedBy,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public record ProjectDocuments(WorkProject Project, bool Archived, int Page, int PageCount, int Total,
    IReadOnlyList<DocumentInfo> Items);
public record DocumentDetails(WorkProject Project, DocumentInfo Document, string Body, IReadOnlyList<ProjectHistory> History);
public record RevisionInfo(int Id, long Version, string Title, string Editor, DateTime SavedAtUtc);
public record DocumentRevisions(WorkProject Project, DocumentInfo Document, int Page, int PageCount, int Total,
    IReadOnlyList<RevisionInfo> Items);
public record DocumentRevisionDetails(WorkProject Project, DocumentInfo Document, RevisionInfo Revision, string Body);
