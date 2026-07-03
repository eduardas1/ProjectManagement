using Workboard.Domain.Entities;
using Workboard.Domain.Enums;

namespace Workboard.Application.WorkItems;

public record WorkProject(int Id, string Key, string Name, bool IsArchived, bool IsOwner);
public record WorkAccount(string Id, string Email);
public record LabelSummary(int Id, string Name, long Version, int UsageCount);
public record RelatedWork(int Id, int Number, string Title, WorkItemType Type, bool IsArchived);
public record WorkItemSummary(int Id, int Number, string Title, WorkItemType Type, WorkItemStatus Status,
    WorkItemPriority Priority, string? Assignee, DateOnly? DueDate, long Version = 0);
public record WorkItemConnection(int TargetId, int Number, string Title, bool IsArchived, WorkItemLinkKind Kind, bool IsOutgoing);
public record WorkHistory(string Actor, DateTime OccurredAtUtc, string Summary, string DetailsJson);
public record WorkItemOptions(WorkProject Project, IReadOnlyList<WorkAccount> Members,
    IReadOnlyList<LabelSummary> Labels, IReadOnlyList<RelatedWork> Items);
public record WorkItemList(WorkProject Project, bool Archived, int Page, int PageCount, int Total,
    IReadOnlyList<WorkItemSummary> Items, WorkItemStatus? Status = null);
public record ProjectLabels(WorkProject Project, IReadOnlyList<LabelSummary> Items);
public record WorkItemDetails(WorkProject Project, WorkItem Item, string Creator, string? Assignee,
    RelatedWork? Parent, RelatedWork? Epic, IReadOnlyList<RelatedWork> Children,
    IReadOnlyList<LabelSummary> Labels, IReadOnlyList<WorkItemConnection> Connections, IReadOnlyList<WorkHistory> History);

public record WorkBoardColumn(WorkItemStatus Status, int Total, IReadOnlyList<WorkItemSummary> Items);
public record WorkBoard(WorkProject Project, IReadOnlyList<WorkBoardColumn> Columns);
