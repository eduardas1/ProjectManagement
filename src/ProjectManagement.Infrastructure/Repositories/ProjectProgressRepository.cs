using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Application.Reports;
using ProjectManagement.Application.Search;
using ProjectManagement.Application.WorkItems;
using ProjectManagement.Domain.Enums;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Infrastructure.Search;

namespace ProjectManagement.Infrastructure.Repositories;

public class ProjectProgressRepository(ApplicationDbContext context, ICurrentUser currentUser,
    ISearchDefinitionValidator validator, WorkItemSearchMapper mapper, ReportingCalendar calendar) : IProjectProgressRepository
{
    private readonly WorkSearchQuery search = new(context, currentUser, validator, mapper);

    public Task<WorkProject?> GetProjectAsync(int projectId, CancellationToken cancellationToken)
        => search.GetProjectAsync(projectId, cancellationToken);

    public async Task<OperationResult<ProjectProgress>> GetProgressAsync(int projectId, WorkSearchInput input, CancellationToken cancellationToken)
    {
        var selected = await search.SelectAsync(projectId, input, cancellationToken);
        if (selected.Status != OperationStatus.Success)
            return new OperationResult<ProjectProgress>(selected.Status, null, selected.Field, selected.Error);
        if (input.Archived) return OperationResult<ProjectProgress>.Invalid(nameof(input.Archived), "Progress reports apply to active work. Switch off archived work first.");
        var selection = selected.Value!;
        var today = calendar.Today;
        var groups = await selection.Items.Where(work => work.Type != WorkItemType.Epic
                && !context.WorkItems.Any(child => child.ProjectId == projectId && child.ParentId == work.Id
                    && child.Type == WorkItemType.Subtask && !child.IsArchived))
            .GroupBy(work => new { work.Status, work.Priority })
            .Select(group => new
            {
                group.Key.Status, group.Key.Priority, Count = group.Count(),
                Overdue = group.Count(work => work.Status != WorkItemStatus.Done && work.Status != WorkItemStatus.Cancelled
                    && work.DueDate.HasValue && work.DueDate.Value < today)
            }).ToListAsync(cancellationToken);
        var states = Enum.GetValues<WorkItemStatus>().ToDictionary(status => status,
            status => groups.Where(group => group.Status == status).Sum(group => group.Count));
        var priorities = Enum.GetValues<WorkItemPriority>().ToDictionary(priority => priority,
            priority => groups.Where(group => group.Priority == priority).Sum(group => group.Count));
        return OperationResult<ProjectProgress>.Success(new ProjectProgress(selection.Project, selection.Expression,
            today, calendar.TimeZoneId, states, priorities, groups.Sum(group => group.Overdue)));
    }
}
