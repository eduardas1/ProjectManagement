using Workboard.Application.WorkItems;

namespace Workboard.Application.Comments;

public record CommentWork(WorkProject Project, int Id, int Number, string Title, bool IsArchived);
public record CommentItem(int Id, string Body, long Version, string Author, DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc, bool IsDeleted, string? DeletedBy, DateTime? DeletedAtUtc, string? ModerationReason,
    bool IsAuthor, bool CanRemove);
public record WorkComments(CommentWork Work, int Page, int PageCount, int Total, IReadOnlyList<CommentItem> Items);
public record WorkComment(CommentWork Work, CommentItem Comment);
