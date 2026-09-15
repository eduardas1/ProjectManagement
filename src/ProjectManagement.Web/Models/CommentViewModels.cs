using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using ProjectManagement.Application.Comments;

namespace ProjectManagement.Web.Models;

public class CreateCommentViewModel
{
    public int ProjectId { get; set; }
    public int WorkItemId { get; set; }
    [Required, StringLength(5000), Display(Name = "Comment")]
    public string Body { get; set; } = "";
    [BindNever, ValidateNever]
    public CommentWork Work { get; set; } = null!;
}

public class EditCommentViewModel : CreateCommentViewModel
{
    public int CommentId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
}

public class RemoveCommentViewModel
{
    public int ProjectId { get; set; }
    public int WorkItemId { get; set; }
    public int CommentId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
    [StringLength(500), Display(Name = "Reason")]
    public string? ModerationReason { get; set; }
    [BindNever, ValidateNever]
    public WorkComment Details { get; set; } = null!;
}
