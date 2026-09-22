using System.ComponentModel.DataAnnotations;

namespace PharmaFlow.Models.ViewModels;

public class OtpViewModel
{
    [Required]
    [RegularExpression(@"^[6-9][0-9]{9}$", ErrorMessage = "Enter a valid mobile number.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the OTP.")]
    [RegularExpression(@"^[0-9]{6}$", ErrorMessage = "OTP must contain 6 digits.")]
    public string Otp { get; set; } = string.Empty;
}
