using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PharmaFlow.Models.ViewModels;

public sealed class AddStaffViewModel : IValidatableObject
{
    [Required, StringLength(120, MinimumLength = 2)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Address { get; set; }

    [Phone, StringLength(20)]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [EmailAddress, StringLength(160)]
    [Display(Name = "Email Address")]
    public string? Email { get; set; }

    [Required, DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Compare(nameof(Password))]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasPhone = !string.IsNullOrWhiteSpace(PhoneNumber);
        var hasEmail = !string.IsNullOrWhiteSpace(Email);

        if (hasPhone == hasEmail)
        {
            yield return new ValidationResult(
                "Enter either a phone number or an email address, not both.",
                new[] { nameof(PhoneNumber), nameof(Email) });
        }
    }

    public string? ProfilePhotoUrl { get; set; }

    [Display(Name = "Profile Photo")]
    public IFormFile? ProfilePhoto { get; set; }
}
