using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Workboard.Application.WorkItems;
using Workboard.Domain.Enums;

namespace Workboard.Web.Models;

public class WorkItemForm
{
    [Range(1, int.MaxValue)]
    public int ProjectId { get; set; }
    [Required, StringLength(200)]
    public string Title { get; set; } = "";
    [StringLength(20000)]
    public string? Description { get; set; }
    [EnumDataType(typeof(WorkItemPriority))]
    public WorkItemPriority Priority { get; set; } = WorkItemPriority.Medium;
    public string? AssigneeId { get; set; }
    public DateOnly? DueDate { get; set; }
    public int[]? LabelIds { get; set; }
    [BindNever, ValidateNever]
    public WorkItemOptions Options { get; set; } = null!;
}

public class CreateWorkItemViewModel : WorkItemForm
{
    [EnumDataType(typeof(WorkItemType))]
    public WorkItemType Type { get; set; } = WorkItemType.Task;
    public int? ParentId { get; set; }
    public int? EpicId { get; set; }
}

public class EditWorkItemViewModel : WorkItemForm
{
    public int WorkItemId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
    [BindNever, ValidateNever]
    public string WorkKey { get; set; } = "";
}
