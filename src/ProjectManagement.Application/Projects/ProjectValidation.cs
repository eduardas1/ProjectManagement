using System.Text.RegularExpressions;

namespace ProjectManagement.Application.Projects;

public static class ProjectValidation
{
    public static string NormalizeKey(string key) => key.Trim().ToUpperInvariant();

    public static bool IsValidKey(string key) => Regex.IsMatch(key, "\\A[A-Z][A-Z0-9]{1,9}\\z", RegexOptions.CultureInvariant);

    public static bool IsValidName(string name) => name.Trim().Length is >= 1 and <= 100;

    public static bool IsValidDescription(string description) => description.Length <= 20000;
}
