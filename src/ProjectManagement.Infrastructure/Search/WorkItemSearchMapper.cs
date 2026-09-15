using Gridify;
using ProjectManagement.Domain.Entities;

namespace ProjectManagement.Infrastructure.Search;

public class WorkItemSearchMapper : GridifyMapper<WorkItem>
{
    public WorkItemSearchMapper() : base(configuration =>
    {
        configuration.CaseSensitive = true;
        configuration.AllowNullSearch = false;
        configuration.EntityFrameworkCompatibilityLayer = true;
        configuration.DisableCollectionNullChecks = true;
    })
    {
        AddMap("type", work => work.Type);
        AddMap("status", work => work.Status);
        AddMap("priority", work => work.Priority);
        AddMap("assignee", work => work.AssigneeId);
        AddMap("label", work => work.Labels.Select(label => label.Label.Name));
        AddMap("deadline", work => work.DueDate);
        AddMap("title", work => work.Title);
        AddMap("description", work => work.Description);
        AddMap("number", work => work.Number);
    }
}
