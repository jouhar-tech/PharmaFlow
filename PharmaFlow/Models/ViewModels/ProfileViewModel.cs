using System.ComponentModel.DataAnnotations;

namespace PharmaFlow.Models.ViewModels;

public sealed class ProfileViewModel
{
    [Required, StringLength(120, MinimumLength = 2)]
    [Display(Name = "Shop / Business Name")]
    public string BusinessName { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 3)]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Phone, StringLength(20)]
    [Display(Name = "Mobile Number")]
    public string? PhoneNumber { get; set; }

    [EmailAddress]
    [Display(Name = "Email")]
    public string? Email { get; set; }
}
