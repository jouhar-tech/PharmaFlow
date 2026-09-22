using System.ComponentModel.DataAnnotations;

namespace PharmaFlow.Models.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Phone number is required")]

        [RegularExpression(
            @"^[6-9][0-9]{9}$",
            ErrorMessage = "Enter a valid 10-digit Indian mobile number")]

        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; } = string.Empty;
    }
}