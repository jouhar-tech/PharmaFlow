using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PharmaFlow.Models.ViewModels;

public sealed class ProfileViewModel
{
    [Required, StringLength(120, MinimumLength = 2)]
    [Display(Name = "Shop / Business Name")]
    public string BusinessName { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 3)]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [StringLength(120)]
    [Display(Name = "Full Name")]
    public string? FullName { get; set; }

    [Phone, StringLength(20)]
    [Display(Name = "Mobile Number")]
    public string? PhoneNumber { get; set; }

    [Required, EmailAddress, StringLength(160)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Shop Address")]
    public string? Address { get; set; }

    public string? ProfilePhotoUrl { get; set; }

    [Display(Name = "Profile Photo")]
    public IFormFile? ProfilePhoto { get; set; }
}
