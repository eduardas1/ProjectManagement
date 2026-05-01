using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Workboard.Application.Documents;
using Workboard.Application.WorkItems;

namespace Workboard.Web.Models;

public class CreateDocumentViewModel
{
    public int ProjectId { get; set; }
    [Required, StringLength(200)]
    public string Title { get; set; } = "";
    [StringLength(100000), DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? Body { get; set; }
    [BindNever, ValidateNever]
    public WorkProject Project { get; set; } = null!;
}

public class EditDocumentViewModel : CreateDocumentViewModel
{
    public int DocumentId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
}

public class ArchiveDocumentViewModel
{
    public int ProjectId { get; set; }
    public int DocumentId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
    [Required]
    public bool? IsArchived { get; set; }
    [BindNever, ValidateNever]
    public DocumentDetails Details { get; set; } = null!;
}
