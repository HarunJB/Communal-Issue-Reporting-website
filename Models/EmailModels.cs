using System.ComponentModel.DataAnnotations;

namespace product_website.Models;

public class EmailOnlyRequest
{
    [Required(ErrorMessage = "Please enter your email.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    public string Email { get; set; } = string.Empty;
}

public class VerifyEmailRequest
{
    [Required(ErrorMessage = "Please enter your email.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter the 6 digit code.")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "The code has 6 digits.")]
    public string Code { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    [Required(ErrorMessage = "Please enter your email.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter the 6 digit code.")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "The code has 6 digits.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a new password.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    public string NewPassword { get; set; } = string.Empty;
}
