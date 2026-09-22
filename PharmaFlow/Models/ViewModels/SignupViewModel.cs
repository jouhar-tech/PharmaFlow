using System.ComponentModel.DataAnnotations;

namespace PharmaFlow.Models.ViewModels;

public class SignupViewModel
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^[6-9][0-9]{9}$", ErrorMessage = "Enter a valid mobile number.")]
    public string PhoneNumber { get; set; } = string.Empty;
}
