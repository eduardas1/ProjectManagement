using ProjectManagement.Application.Common;
using ProjectManagement.Application.WorkItems;

namespace ProjectManagement.Application.Comments;

public record CommentWork(WorkProject Project, int Id, int Number, string Title, bool IsArchived);
public record CommentItem(int Id, string Body, long Version, PersonSummary Author, DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc, bool IsDeleted, PersonSummary? DeletedBy, DateTime? DeletedAtUtc, string? ModerationReason,
    bool IsAuthor, bool CanRemove);
public record WorkComments(CommentWork Work, int Page, int PageCount, int Total, IReadOnlyList<CommentItem> Items);
public record WorkComment(CommentWork Work, CommentItem Comment);
