using System.ComponentModel.DataAnnotations;
using Workboard.Application.Projects;

namespace Workboard.Web.Models;

public class AddMemberViewModel
{
    public int ProjectId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";
    public string ProjectKey { get; set; } = "";
}

public class RemoveMemberViewModel
{
    public int ProjectId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
    [Required]
    public string UserId { get; set; } = "";
    public string ProjectKey { get; set; } = "";
    public string Email { get; set; } = "";
}

public class TransferOwnershipViewModel
{
    public int ProjectId { get; set; }
    [Range(1, long.MaxValue)]
    public long ExpectedVersion { get; set; }
    [Required, Display(Name = "New owner")]
    public string UserId { get; set; } = "";
    public string ProjectKey { get; set; } = "";
    public IReadOnlyList<ProjectParticipant> Participants { get; set; } = [];
}
