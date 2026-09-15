namespace ProjectManagement.Application.Interfaces;

public interface ISearchDefinitionValidator
{
    string? ValidateExpression(string expression);
    string? ValidateOrdering(string ordering);
}
