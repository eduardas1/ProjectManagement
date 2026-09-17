using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ProjectManagement.Application.Common;

public record PersonSummary(string Id, string FirstName, string LastName, string Email)
{
    public PersonSummary() : this("", "", "", "") { }
    private static readonly string[] Colors = ["#337ab7", "#3c763d", "#6f5499", "#246b6b", "#8a6d3b", "#526478", "#a94442", "#5e6875"];

    public string DisplayName => string.IsNullOrWhiteSpace(FirstName + LastName)
        ? (string.IsNullOrWhiteSpace(Email) ? "Unavailable account" : Email)
        : (FirstName.Trim() + " " + LastName.Trim()).Trim();
    public string Initial => StringInfo.GetNextTextElement(DisplayName).ToUpperInvariant();
    public string Color => Colors[SHA256.HashData(Encoding.UTF8.GetBytes(Id))[0] % Colors.Length];
    public string ChoiceLabel => DisplayName == Email ? Email : DisplayName + " (" + Email + ")";
}
