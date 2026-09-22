using System.ComponentModel.DataAnnotations;

namespace PharmaFlow.Models.ViewModels;

public class SignupViewModel
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^[6-9][0-9]{9}$", ErrorMessage = "Enter a valid 10-digit Indian mobile number.")]
    [Display(Name = "Phone number")]
    public string PhoneNumber { get; set; } = string.Empty;
}
