using ProjectManagement.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using ProjectManagement.Application.Documents;
using ProjectManagement.Application.WorkItems;

namespace ProjectManagement.Web.Models;

public class CreateDocumentViewModel
{
    public int ProjectId { get; set; }
    [Required, StringLength(200)]
    public string Title { get; set; } = "";
    [StringLength(100000), DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? Body { get; set; }
    [EnumDataType(typeof(DocumentFormat))]
    public DocumentFormat Format { get; set; }
    [BindNever, ValidateNever]
    public WorkProject Project { get; set; } = null!;
}

public class EditDocumentViewModel : CreateDocumentViewModel
{
    public int DocumentId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
}


public class DocumentPreviewViewModel
{
    public int ProjectId { get; set; }
    [StringLength(100000), DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? Body { get; set; }
    [EnumDataType(typeof(DocumentFormat))]
    public DocumentFormat Format { get; set; }
}
