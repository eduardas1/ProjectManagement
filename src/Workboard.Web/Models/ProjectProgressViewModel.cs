using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Workboard.Application.Reports;
using Workboard.Application.Search;
using Workboard.Application.WorkItems;

namespace Workboard.Web.Models;

public class ProjectProgressViewModel
{
    public WorkSearchInput Input { get; set; } = new();
    [BindNever, ValidateNever]
    public WorkProject Project { get; set; } = null!;
    [BindNever, ValidateNever]
    public ProjectProgress? Progress { get; set; }

    public Dictionary<string, string> SearchRoutes() => WorkSearchRoutes.Create(Project.Id, Input, Input.Page);
}
