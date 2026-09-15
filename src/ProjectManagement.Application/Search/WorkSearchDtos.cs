using ProjectManagement.Application.WorkItems;
using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Application.Search;

public class WorkSearchInput
{
    public string? Text { get; set; }
    public WorkItemType? Type { get; set; }
    public WorkItemStatus? Status { get; set; }
    public WorkItemPriority? Priority { get; set; }
    public string? AssigneeId { get; set; }
    public bool UnassignedOnly { get; set; }
    public string? Label { get; set; }
    public DateOnly? DeadlineFrom { get; set; }
    public DateOnly? DeadlineThrough { get; set; }
    public string? Expression { get; set; }
    public string? Ordering { get; set; }
    public bool Archived { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public record WorkSearchOptions(WorkProject Project, IReadOnlyList<WorkAccount> Members, IReadOnlyList<string> Labels);
public record WorkSearchPage(WorkProject Project, string Expression, string Ordering, bool Archived, int Page,
    int PageSize, int PageCount, int Total, IReadOnlyList<WorkItemSummary> Items);
