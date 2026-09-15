using ProjectManagement.Application.WorkItems;
using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Application.Reports;

public record ProjectProgress(WorkProject Project, string Expression, DateOnly ReportDate, string TimeZoneId,
    IReadOnlyDictionary<WorkItemStatus, int> States, IReadOnlyDictionary<WorkItemPriority, int> Priorities, int Overdue)
{
    public int EligibleTotal => States.Values.Sum();
    public int Cancelled => States[WorkItemStatus.Cancelled];
    public int Completed => States[WorkItemStatus.Done];
    public int CompletionTotal => EligibleTotal - Cancelled;
    public decimal? CompletionPercent => CompletionTotal == 0 ? null : Math.Round(100m * Completed / CompletionTotal, 1, MidpointRounding.AwayFromZero);
}
