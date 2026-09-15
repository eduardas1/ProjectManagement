using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using ProjectManagement.Application.Attachments;

namespace ProjectManagement.Web.Models;

public class UploadAttachmentViewModel
{
    public int ProjectId { get; set; }
    public int? WorkItemId { get; set; }
    public int? DocumentId { get; set; }
    [Required]
    public IFormFile? File { get; set; }
    [BindNever, ValidateNever]
    public AttachmentTarget Target { get; set; } = null!;
}

public class RemoveAttachmentViewModel
{
    public int ProjectId { get; set; }
    public int AttachmentId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
    [BindNever, ValidateNever]
    public AttachmentDetails Details { get; set; } = null!;
}
