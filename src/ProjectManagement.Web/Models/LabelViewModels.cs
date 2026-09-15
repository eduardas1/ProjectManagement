using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using ProjectManagement.Application.WorkItems;

namespace ProjectManagement.Web.Models;

public class CreateLabelViewModel
{
    public int ProjectId { get; set; }
    [Required, StringLength(40)]
    public string Name { get; set; } = "";
    [BindNever, ValidateNever]
    public WorkProject Project { get; set; } = null!;
}

public class RenameLabelViewModel : CreateLabelViewModel
{
    public int LabelId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
}

public class DeleteLabelViewModel
{
    public int ProjectId { get; set; }
    public int LabelId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
    [BindNever, ValidateNever]
    public WorkProject Project { get; set; } = null!;
    [BindNever, ValidateNever]
    public string Name { get; set; } = "";
    [BindNever, ValidateNever]
    public int UsageCount { get; set; }
}
