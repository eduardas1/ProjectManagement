using System.Text.Json;
using ProjectManagement.Application.Interfaces;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Application.WorkItems;

public static class WorkItemChanges
{
    public static bool IsClosed(WorkItemStatus status) => status is WorkItemStatus.Done or WorkItemStatus.Cancelled;

    public static void Record(IProjectRepository repository, WorkItem work, string actorId, DateTime now,
        string action, string summary, object details)
    {
        repository.AddEvent(new ChangeEvent
        {
            ProjectId = work.ProjectId, WorkItemId = work.Id, ActorId = actorId, OccurredAtUtc = now,
            Action = action, Summary = summary, DetailsJson = JsonSerializer.Serialize(details)
        });
    }

    public static void Touch(WorkItem work, DateTime now)
    {
        work.Version = checked(work.Version + 1);
        work.UpdatedAtUtc = now;
    }
}
