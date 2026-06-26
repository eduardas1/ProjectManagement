using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Workboard.Application.Search;
using Workboard.Application.WorkItems;

namespace Workboard.Web.Models;

public class SavedSearchFormViewModel
{
    public int ProjectId { get; set; }
    public int Id { get; set; }
    public long ExpectedVersion { get; set; }
    [StringLength(100)]
    public string? Name { get; set; }
    [StringLength(2000)]
    public string? Expression { get; set; }
    [StringLength(200)]
    public string? Ordering { get; set; }
    public bool Archived { get; set; }
    [StringLength(ReturnNavigation.MaxLength)]
    public string? ReturnUrl { get; set; }
    [BindNever, ValidateNever]
    public WorkProject Project { get; set; } = null!;
    [BindNever, ValidateNever]
    public string Action { get; set; } = "Create";
    public string Title => Action switch { "Rename" => "Rename search", "Delete" => "Delete search", _ => "Save search" };
}

public record SavedSearchListViewModel(SavedSearchList Searches, string? ReturnUrl);
