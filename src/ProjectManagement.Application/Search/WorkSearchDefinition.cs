using System.Globalization;
using System.Text;
using ProjectManagement.Application.Common;
using ProjectManagement.Application.Projects;

namespace ProjectManagement.Application.Search;

public static class WorkSearchDefinition
{
    public static OperationResult<string> Build(string projectKey, WorkSearchInput input)
    {
        var text = (input.Text ?? "").Trim();
        var assignee = (input.AssigneeId ?? "").Trim();
        var label = (input.Label ?? "").Trim();
        var advanced = (input.Expression ?? "").Trim();
        if (text.Length > 200 || text.Any(char.IsControl)) return Invalid(nameof(input.Text), "Use at most 200 characters on one line.");
        if (assignee.Length > 450 || assignee.Any(char.IsControl)) return Invalid(nameof(input.AssigneeId), "Select a valid assignee.");
        if (label.Length > 40 || label.Any(char.IsControl)) return Invalid(nameof(input.Label), "Use a label of at most 40 characters on one line.");
        if (input.UnassignedOnly && assignee.Length > 0) return Invalid(nameof(input.AssigneeId), "Choose an assignee or unassigned work, not both.");
        if ((input.Type.HasValue && !Enum.IsDefined(input.Type.Value)) || (input.Status.HasValue && !Enum.IsDefined(input.Status.Value))
            || (input.Priority.HasValue && !Enum.IsDefined(input.Priority.Value))) return Invalid("", "Select valid type, status and priority values.");
        if (input.DeadlineFrom > input.DeadlineThrough) return Invalid(nameof(input.DeadlineThrough), "The end date must be on or after the start date.");
        if (input.Page < 1) return Invalid(nameof(input.Page), "Use a positive page number.");
        if (input.PageSize is < 1 or > 100) return Invalid(nameof(input.PageSize), "Use a page size from 1 to 100.");
        try { _ = checked((input.Page - 1) * input.PageSize); }
        catch (OverflowException) { return Invalid(nameof(input.Page), "This page number is too large."); }
        if (advanced.Length > 2000) return Invalid(nameof(input.Expression), "Use at most 2000 characters for the combined expression.");

        var parts = new List<string>();
        if (text.Length > 0)
        {
            var separator = text.LastIndexOf('-');
            var prefix = separator > 0 ? text[..separator].ToUpperInvariant() : "";
            var suffix = separator > 0 ? text[(separator + 1)..] : "";
            if (ProjectValidation.IsValidKey(prefix) && suffix.Length > 0 && suffix.All(character => character is >= '0' and <= '9'))
            {
                if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
                    return Invalid(nameof(input.Text), "Use a valid positive work number.");
                parts.Add("number=" + (prefix == projectKey ? number : 0).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                var caseFlag = text.All(character => character <= 127) ? "/i" : "";
                parts.Add("title=*" + Escape(text) + caseFlag + "|description=*" + Escape(text) + caseFlag);
            }
        }
        if (input.Type.HasValue) parts.Add("type=" + input.Type.Value);
        if (input.Status.HasValue) parts.Add("status=" + input.Status.Value);
        if (input.Priority.HasValue) parts.Add("priority=" + input.Priority.Value);
        if (input.UnassignedOnly) parts.Add("assignee=");
        else if (assignee.Length > 0) parts.Add("assignee=" + Escape(assignee));
        if (label.Length > 0) parts.Add("label=" + Escape(label));
        if (input.DeadlineFrom.HasValue) parts.Add("deadline>=" + input.DeadlineFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (input.DeadlineThrough.HasValue) parts.Add("deadline<=" + input.DeadlineThrough.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (advanced.Length > 0) parts.Add(advanced);
        var expression = parts.Count switch { 0 => "", 1 => parts[0], _ => string.Join(",", parts.Select(part => "(" + part + ")")) };
        return expression.Length > 2000 ? Invalid(nameof(input.Expression), "Use at most 2000 characters for the combined expression.")
            : OperationResult<string>.Success(expression);
    }

    private static OperationResult<string> Invalid(string field, string message) => OperationResult<string>.Invalid(field, message);

    private static string Escape(string value)
    {
        var result = new StringBuilder();
        foreach (var character in value)
        {
            if ("\\(),|/^$=!<>".Contains(character)) result.Append('\\');
            result.Append(character);
        }
        return result.ToString();
    }
}
