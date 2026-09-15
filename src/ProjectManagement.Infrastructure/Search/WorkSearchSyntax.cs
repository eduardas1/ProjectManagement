using System.Globalization;
using Gridify.Syntax;
using ProjectManagement.Domain.Enums;
using ProjectManagement.Application.Interfaces;

namespace ProjectManagement.Infrastructure.Search;

public class WorkSearchSyntax : ISearchDefinitionValidator
{
    public string? ValidateExpression(string expression)
    {
        if (expression.Length > 2000) return "Use at most 2000 characters for the combined expression.";
        if (expression.Any(char.IsControl)) return "Use a single line for the expression.";
        if (string.IsNullOrWhiteSpace(expression)) return null;
        var lexer = new Lexer(expression, []);
        var nesting = 0;
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == SyntaxKind.End) break;
            if (token.Kind == SyntaxKind.OpenParenthesisToken && ++nesting > 8) return "Use at most eight nested groups.";
            if (token.Kind == SyntaxKind.CloseParenthesis && --nesting < 0) return "Check the expression syntax.";
        }
        if (nesting != 0 || lexer.Diagnostics.Any()) return "Check the expression syntax.";
        var trailingEscapes = expression.Reverse().TakeWhile(c => c == '\\').Count();
        if (trailingEscapes % 2 != 0) return "Complete the escaped value.";
        var tree = SyntaxTree.Parse(expression, []);
        if (tree.Diagnostics.Any()) return "Check the expression syntax.";
        var pending = new Stack<ExpressionSyntax>();
        pending.Push(tree.Root);
        var comparisons = 0;
        while (pending.TryPop(out var node))
        {
            if (node is ParenthesizedExpressionSyntax group) { pending.Push(group.Expression); continue; }
            if (node is not BinaryExpressionSyntax binary) return "Check the expression syntax.";
            if (binary.OperatorToken.Kind is SyntaxKind.And or SyntaxKind.Or)
            {
                pending.Push(binary.Left); pending.Push(binary.Right); continue;
            }
            if (++comparisons > 32) return "Use at most 32 comparisons.";
            if (binary.Left is not FieldExpressionSyntax field || binary.Right is not ValueExpressionSyntax value
                || field.Indexer != null) return "Use a supported field comparison.";
            var error = ValidateComparison(field.FieldToken.Text, binary.OperatorToken.Kind, value);
            if (error != null) return error;
        }
        return null;
    }

    private static string? ValidateComparison(string field, SyntaxKind op, ValueExpressionSyntax value)
    {
        var text = value.ValueToken.Text ?? "";
        if (value.IsCaseInsensitive && text.Any(character => character > 127))
            return "Use exact text without /i for non-ASCII letters.";
        var equality = op is SyntaxKind.Equal or SyntaxKind.NotEqual;
        var comparison = equality || op is SyntaxKind.LessThan or SyntaxKind.LessOrEqualThan or SyntaxKind.GreaterThan or SyntaxKind.GreaterOrEqualThan;
        var positiveText = op is SyntaxKind.Equal or SyntaxKind.Like or SyntaxKind.StartsWith or SyntaxKind.EndsWith;
        var stringOp = positiveText || op is SyntaxKind.NotEqual or SyntaxKind.NotLike or SyntaxKind.NotStartsWith or SyntaxKind.NotEndsWith;
        if (field is "title" or "description") return stringOp ? null : "Use a supported text operator.";
        if (field == "label") return positiveText && !value.IsNullOrDefault ? null : "Labels support positive =, =*, ^ and $ comparisons with a value.";
        if (value.IsCaseInsensitive) return "Use /i only with title, description or label.";
        if (field == "assignee") return equality ? null : "Assignees support = and != with an account ID or an empty value.";
        if (field == "deadline")
            return comparison && ((value.IsNullOrDefault && equality) || DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                ? null : "Use an ISO deadline (yyyy-MM-dd), or = / != with an empty value.";
        if (field == "number") return comparison && !value.IsNullOrDefault && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            ? null : "Compare number with a valid integer.";
        var names = field switch { "type" => Enum.GetNames<WorkItemType>(), "status" => Enum.GetNames<WorkItemStatus>(), "priority" => Enum.GetNames<WorkItemPriority>(), _ => null };
        if (names == null) return "Use one of the listed search fields.";
        return equality && names.Contains(text, StringComparer.Ordinal) ? null : "Use = or != with a listed enum name.";
    }

    public string? ValidateOrdering(string ordering)
    {
        if (ordering.Length > 200) return "Use at most 200 characters for ordering.";
        if (string.IsNullOrWhiteSpace(ordering)) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in ordering.Split(','))
        {
            var words = part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is < 1 or > 2 || words[0] is not ("number" or "title" or "deadline")
                || (words.Length == 2 && words[1] is not ("asc" or "desc")) || !seen.Add(words[0]))
                return "Order by number, title or deadline, optionally asc or desc, without duplicates.";
        }
        return null;
    }
}
