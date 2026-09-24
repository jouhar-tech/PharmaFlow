using System.ComponentModel.DataAnnotations;

namespace PharmaFlow.Models.ViewModels;

public sealed class BusinessSetupViewModel
{
    [Required(ErrorMessage = "Business name is required.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Business name must be between 2 and 120 characters.")]
    [Display(Name = "Business name")]
    public string BusinessName { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Enter a valid mobile number.")]
    [StringLength(20)]
    [Display(Name = "Mobile number")]
    public string? PhoneNumber { get; set; }
}
