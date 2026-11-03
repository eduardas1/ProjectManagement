using ProjectManagement.Application.Common;
using MediatR;
using ProjectManagement.Application.Interfaces;

namespace ProjectManagement.Application.Attachments;

public record GetAttachmentTargetQuery(int ProjectId, int? WorkItemId, int? DocumentId) : IRequest<AttachmentTarget?>;
public record ListAttachmentsQuery(int ProjectId, int? WorkItemId, int? DocumentId, int Page = 1, TableOptions? Table = null) : IRequest<TargetAttachments?>;
public record GetAttachmentQuery(int ProjectId, int AttachmentId) : IRequest<AttachmentDetails?>;
public record DownloadAttachmentQuery(int ProjectId, int AttachmentId) : IRequest<AttachmentDownload?>;

public class GetAttachmentTargetHandler(ICollaborationReadRepository repository) : IRequestHandler<GetAttachmentTargetQuery, AttachmentTarget?>
{
    public Task<AttachmentTarget?> Handle(GetAttachmentTargetQuery request, CancellationToken cancellationToken)
        => repository.GetAttachmentTargetAsync(request.ProjectId, request.WorkItemId, request.DocumentId, cancellationToken);
}

public class ListAttachmentsHandler(ICollaborationReadRepository repository) : IRequestHandler<ListAttachmentsQuery, TargetAttachments?>
{
    public Task<TargetAttachments?> Handle(ListAttachmentsQuery request, CancellationToken cancellationToken)
        => repository.ListAttachmentsAsync(request.ProjectId, request.WorkItemId, request.DocumentId, request.Page, cancellationToken, request.Table);
}

public class GetAttachmentHandler(ICollaborationReadRepository repository) : IRequestHandler<GetAttachmentQuery, AttachmentDetails?>
{
    public Task<AttachmentDetails?> Handle(GetAttachmentQuery request, CancellationToken cancellationToken)
        => repository.GetAttachmentAsync(request.ProjectId, request.AttachmentId, cancellationToken);
}

public class DownloadAttachmentHandler(ICollaborationReadRepository repository) : IRequestHandler<DownloadAttachmentQuery, AttachmentDownload?>
{
    public Task<AttachmentDownload?> Handle(DownloadAttachmentQuery request, CancellationToken cancellationToken)
        => repository.DownloadAttachmentAsync(request.ProjectId, request.AttachmentId, cancellationToken);
}
