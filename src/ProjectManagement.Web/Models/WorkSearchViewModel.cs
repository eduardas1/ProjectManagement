using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using ProjectManagement.Application.Search;

namespace ProjectManagement.Web.Models;

public class WorkSearchViewModel
{
    public WorkSearchInput Input { get; set; } = new();
    [BindNever, ValidateNever]
    public WorkSearchOptions Options { get; set; } = null!;
    [BindNever, ValidateNever]
    public WorkSearchPage? Results { get; set; }

    public Dictionary<string, string> Routes(int page) => WorkSearchRoutes.Create(Options.Project.Id, Input, page);
}
