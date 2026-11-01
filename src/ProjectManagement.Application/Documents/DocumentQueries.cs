using MediatR;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Application.WorkItems;

namespace ProjectManagement.Application.Documents;

public record GetDocumentProjectQuery(int ProjectId) : IRequest<WorkProject?>;
public record ListDocumentsQuery(int ProjectId, int Page = 1) : IRequest<ProjectDocuments?>;
public record GetDocumentQuery(int ProjectId, int DocumentId) : IRequest<DocumentDetails?>;
public record ListRevisionsQuery(int ProjectId, int DocumentId, int Page = 1) : IRequest<DocumentRevisions?>;
public record GetRevisionQuery(int ProjectId, int DocumentId, int RevisionId) : IRequest<DocumentRevisionDetails?>;

public class GetDocumentProjectHandler(ICollaborationReadRepository repository) : IRequestHandler<GetDocumentProjectQuery, WorkProject?>
{
    public Task<WorkProject?> Handle(GetDocumentProjectQuery request, CancellationToken cancellationToken)
        => repository.GetDocumentProjectAsync(request.ProjectId, cancellationToken);
}

public class ListDocumentsHandler(ICollaborationReadRepository repository) : IRequestHandler<ListDocumentsQuery, ProjectDocuments?>
{
    public Task<ProjectDocuments?> Handle(ListDocumentsQuery request, CancellationToken cancellationToken)
        => repository.ListDocumentsAsync(request.ProjectId, request.Page, cancellationToken);
}

public class GetDocumentHandler(ICollaborationReadRepository repository) : IRequestHandler<GetDocumentQuery, DocumentDetails?>
{
    public Task<DocumentDetails?> Handle(GetDocumentQuery request, CancellationToken cancellationToken)
        => repository.GetDocumentAsync(request.ProjectId, request.DocumentId, cancellationToken);
}

public class ListRevisionsHandler(ICollaborationReadRepository repository) : IRequestHandler<ListRevisionsQuery, DocumentRevisions?>
{
    public Task<DocumentRevisions?> Handle(ListRevisionsQuery request, CancellationToken cancellationToken)
        => repository.ListRevisionsAsync(request.ProjectId, request.DocumentId, request.Page, cancellationToken);
}

public class GetRevisionHandler(ICollaborationReadRepository repository) : IRequestHandler<GetRevisionQuery, DocumentRevisionDetails?>
{
    public Task<DocumentRevisionDetails?> Handle(GetRevisionQuery request, CancellationToken cancellationToken)
        => repository.GetRevisionAsync(request.ProjectId, request.DocumentId, request.RevisionId, cancellationToken);
}
