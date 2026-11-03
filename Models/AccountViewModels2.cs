using System.ComponentModel.DataAnnotations;

namespace product_website.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "Please enter your email.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Please enter a valid phone number.")]
    [StringLength(30)]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your password.")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [RegularExpression("^(bs|en)$", ErrorMessage = "Please choose a language.")]
    public string Language { get; set; } = "bs";
}

public class SuccessViewModel
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
