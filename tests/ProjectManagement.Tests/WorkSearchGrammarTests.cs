using Gridify;
using ProjectManagement.Domain.Entities;
using ProjectManagement.Application.Search;
using ProjectManagement.Infrastructure.Search;

namespace ProjectManagement.Tests;

public class WorkSearchGrammarTests
{
    [Fact]
    public void ClientMetadataListsExactlyTheFieldsAndOperatorsTheValidatorAccepts()
    {
        // Arrange
        var validator = new WorkSearchSyntax();
        var samples = new Dictionary<string, string> {
            ["type"] = "Task", ["status"] = "Todo", ["priority"] = "High",
            ["assignee"] = "account-1", ["label"] = "billing", ["deadline"] = "2026-06-18",
            ["title"] = "Payment", ["description"] = "Delivery", ["number"] = "12"
        };

        // Act
        var expressions = WorkSearchGrammar.Fields.SelectMany(field => field.Operators.Select(op => field.Name + op + samples[field.Name])).ToArray();
        var rejected = new[] { "label!=billing", "status>*Todo", "assignee=*account", "unknown=1", "deadline^2026" };

        // Assert
        Assert.Equal(samples.Keys.Order(), WorkSearchGrammar.Fields.Select(field => field.Name).Order());
        Assert.All(expressions, expression =>
        {
            Assert.Null(validator.ValidateExpression(expression));
            Assert.Empty(Array.Empty<WorkItem>().AsQueryable().ApplyFiltering(expression, new WorkItemSearchMapper()).ToArray());
        });
        Assert.All(rejected, expression => Assert.NotNull(validator.ValidateExpression(expression)));
        Assert.Null(validator.ValidateExpression("assignee="));
        Assert.Null(validator.ValidateExpression("deadline="));
        Assert.NotNull(validator.ValidateExpression("number="));
    }
}
