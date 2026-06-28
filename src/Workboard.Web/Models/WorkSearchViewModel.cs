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

    public Dictionary<string, string> Routes(int page) => WorkSearchRoutes.Create(Options.Project.Id, Input, page);
}
