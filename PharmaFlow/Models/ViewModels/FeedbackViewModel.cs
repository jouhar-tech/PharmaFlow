using System.ComponentModel.DataAnnotations;

namespace PharmaFlow.Models.ViewModels;

public sealed class FeedbackViewModel
{
    [Required(ErrorMessage = "Please enter your feedback.")]
    [StringLength(2000, MinimumLength = 5, ErrorMessage = "Feedback must be between 5 and 2000 characters.")]
    [Display(Name = "Feedback")]
    public string Message { get; set; } = string.Empty;
}
