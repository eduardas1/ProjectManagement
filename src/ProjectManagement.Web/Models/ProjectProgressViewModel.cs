using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using ProjectManagement.Application.Reports;
using ProjectManagement.Application.Search;
using ProjectManagement.Application.WorkItems;

namespace ProjectManagement.Web.Models;

public class ProjectProgressViewModel
{
    public WorkSearchInput Input { get; set; } = new();
    [BindNever, ValidateNever]
    public WorkProject Project { get; set; } = null!;
    [BindNever, ValidateNever]
    public ProjectProgress? Progress { get; set; }

    public Dictionary<string, string> SearchRoutes() => WorkSearchRoutes.Create(Project.Id, Input, Input.Page);
}
