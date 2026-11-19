using System.ComponentModel.DataAnnotations;

namespace PharmaFlow.Models.ViewModels;

public sealed class BusinessSetupViewModel
{
    [Required(ErrorMessage = "Business name is required.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Business name must be between 2 and 120 characters.")]
    [Display(Name = "Business name")]
    public string BusinessName { get; set; } = string.Empty;

    [StringLength(10, ErrorMessage = "Mobile number must be exactly 10 digits.")]
    [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Enter exactly 10 digits using numbers only.")]
    [Display(Name = "Mobile number")]
    public string? PhoneNumber { get; set; }
}
