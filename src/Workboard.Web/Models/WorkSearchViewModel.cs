using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Workboard.Application.Search;

namespace Workboard.Web.Models;

public class WorkSearchViewModel
{
    public WorkSearchInput Input { get; set; } = new();
    [BindNever, ValidateNever]
    public WorkSearchOptions Options { get; set; } = null!;
    [BindNever, ValidateNever]
    public WorkSearchPage? Results { get; set; }
    [BindNever, ValidateNever]
    public SavedSearchList? SavedSearches { get; set; }

    public Dictionary<string, string> Routes(int page)
    {
        var routes = new Dictionary<string, string>
        {
            ["projectId"] = Options.Project.Id.ToString(CultureInfo.InvariantCulture),
            ["Input.Page"] = page.ToString(CultureInfo.InvariantCulture),
            ["Input.PageSize"] = Input.PageSize.ToString(CultureInfo.InvariantCulture),
            ["Input.Archived"] = Input.Archived.ToString().ToLowerInvariant(),
            ["Input.UnassignedOnly"] = Input.UnassignedOnly.ToString().ToLowerInvariant()
        };
        void Add(string key, string? value) { if (!string.IsNullOrEmpty(value)) routes["Input." + key] = value; }
        Add("Text", Input.Text); Add("Type", Input.Type?.ToString()); Add("Status", Input.Status?.ToString());
        Add("Priority", Input.Priority?.ToString()); Add("AssigneeId", Input.AssigneeId); Add("Label", Input.Label);
        Add("DeadlineFrom", Input.DeadlineFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("DeadlineThrough", Input.DeadlineThrough?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("Expression", Input.Expression); Add("Ordering", Input.Ordering);
        return routes;
    }
}
