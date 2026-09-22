using System.ComponentModel.DataAnnotations;

namespace PharmaFlow.Models.ViewModels;

public class LoginViewModel
{
    [Required, StringLength(100, MinimumLength = 2)]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}
