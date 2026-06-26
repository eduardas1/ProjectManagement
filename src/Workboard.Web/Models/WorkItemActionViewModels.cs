using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Workboard.Application.WorkItems;
using Workboard.Domain.Enums;

namespace Workboard.Web.Models;

public class WorkItemActionViewModel
{
    public int ProjectId { get; set; }
    public int WorkItemId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
    [BindNever, ValidateNever]
    public WorkItemDetails Details { get; set; } = null!;
}

public class WorkItemHierarchyViewModel : WorkItemActionViewModel
{
    public int? ParentId { get; set; }
    public int? EpicId { get; set; }
    [BindNever, ValidateNever]
    public WorkItemOptions Options { get; set; } = null!;
}

public class WorkItemLinkViewModel : WorkItemActionViewModel
{
    [Range(1, int.MaxValue)]
    public int TargetId { get; set; }
    [EnumDataType(typeof(WorkItemLinkKind))]
    public WorkItemLinkKind Kind { get; set; }
    [BindNever, ValidateNever]
    public WorkItemOptions Options { get; set; } = null!;
    [BindNever, ValidateNever]
    public string TargetName { get; set; } = "";
}

public class WorkItemArchiveViewModel : WorkItemActionViewModel
{
    public bool IsArchived { get; set; }
}

public class WorkItemStatusViewModel : WorkItemActionViewModel
{
    [Required, EnumDataType(typeof(WorkItemStatus))]
    public WorkItemStatus? Status { get; set; }
    [StringLength(ReturnNavigation.MaxLength)]
    public string? ReturnUrl { get; set; }
}
