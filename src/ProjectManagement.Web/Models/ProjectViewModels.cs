using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Web.Models;

public class CreateProjectViewModel
{
    [Required, StringLength(10, MinimumLength = 2)]
    public string Key { get; set; } = "";
    [Required, StringLength(100)]
    public string Name { get; set; } = "";
    [StringLength(20000)]
    public string? Description { get; set; }
}

public class EditProjectViewModel
{
    public int ProjectId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
    [Required, StringLength(100)]
    public string Name { get; set; } = "";
    [StringLength(20000)]
    public string? Description { get; set; }
    public string Key { get; set; } = "";
}
