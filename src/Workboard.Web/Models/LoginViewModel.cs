using System.ComponentModel.DataAnnotations;

namespace Workboard.Web.Models;

public class LoginViewModel
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(1024)]
    public string Password { get; set; } = "";

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
