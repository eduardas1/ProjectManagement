using System.Globalization;
using ProjectManagement.Application.Search;

namespace ProjectManagement.Web.Models;

public static class WorkSearchRoutes
{
    public static Dictionary<string, string> Create(int projectId, WorkSearchInput input, int page)
    {
        var routes = new Dictionary<string, string>
        {
            ["projectId"] = projectId.ToString(CultureInfo.InvariantCulture),
            ["Input.Page"] = page.ToString(CultureInfo.InvariantCulture),
            ["Input.PageSize"] = input.PageSize.ToString(CultureInfo.InvariantCulture),
            ["Input.UnassignedOnly"] = input.UnassignedOnly.ToString().ToLowerInvariant()
        };
        void Add(string key, string? value) { if (!string.IsNullOrEmpty(value)) routes["Input." + key] = value; }
        Add("Text", input.Text); Add("Type", input.Type?.ToString()); Add("Status", input.Status?.ToString());
        Add("Priority", input.Priority?.ToString()); Add("AssigneeId", input.AssigneeId); Add("Label", input.Label);
        Add("DeadlineFrom", input.DeadlineFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("DeadlineThrough", input.DeadlineThrough?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("Expression", input.Expression); Add("Ordering", input.Ordering);
        return routes;
    }
}
