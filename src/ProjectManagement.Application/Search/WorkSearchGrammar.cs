using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Application.Search;

public record WorkSearchField(string Name, string Kind, IReadOnlyList<string> Operators, IReadOnlyList<string> Values);

public static class WorkSearchGrammar
{
    private static readonly string[] Equality = ["=", "!="];
    private static readonly string[] Numeric = ["=", "!=", "<", "<=", ">", ">="];
    private static readonly string[] Text = ["=", "!=", "=*", "!*", "^", "!^", "$", "!$"];

    public static IReadOnlyList<WorkSearchField> Fields { get; } = [
        new("type", "enum", Equality, Enum.GetNames<WorkItemType>()),
        new("status", "enum", Equality, Enum.GetNames<WorkItemStatus>()),
        new("priority", "enum", Equality, Enum.GetNames<WorkItemPriority>()),
        new("assignee", "account", Equality, []),
        new("label", "label", ["=", "=*", "^", "$"], []),
        new("deadline", "date", Numeric, []),
        new("title", "text", Text, []),
        new("description", "text", Text, []),
        new("number", "number", Numeric, [])
    ];
}
