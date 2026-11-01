using ProjectManagement.Domain.Enums;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Projects;
using ProjectManagement.Application.WorkItems;

namespace ProjectManagement.Application.Documents;

public record DocumentInfo(int Id, string Title, long Version, bool IsArchived, PersonSummary CreatedBy, PersonSummary UpdatedBy,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public record ProjectDocuments(WorkProject Project, int Page, int PageCount, int Total,
    IReadOnlyList<DocumentInfo> Items);
public record DocumentDetails(WorkProject Project, DocumentInfo Document, string Body, IReadOnlyList<ProjectHistory> History, DocumentFormat Format = DocumentFormat.PlainText);
public record RevisionInfo(int Id, long Version, string Title, PersonSummary Editor, DateTime SavedAtUtc);
public record DocumentRevisions(WorkProject Project, DocumentInfo Document, int Page, int PageCount, int Total,
    IReadOnlyList<RevisionInfo> Items);
public record DocumentRevisionDetails(WorkProject Project, DocumentInfo Document, RevisionInfo Revision, string Body, DocumentFormat Format = DocumentFormat.PlainText);
