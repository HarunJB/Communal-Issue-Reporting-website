using System.ComponentModel.DataAnnotations;

namespace product_website.Models;

// ----- Requests (what the mobile app sends) -----

public class LoginRequest
{
    [Required(ErrorMessage = "Please enter your email.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your password.")]
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(100)]
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
    public string Password { get; set; } = string.Empty;

    [RegularExpression("^(bs|en)$", ErrorMessage = "Please choose a language.")]
    public string Language { get; set; } = "bs";
}

public class UpdateProfileRequest
{
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [StringLength(100)]
    public string? DisplayName { get; set; }

    [Phone(ErrorMessage = "Please enter a valid phone number.")]
    [StringLength(30)]
    public string? Phone { get; set; }

    [RegularExpression("^(bs|en)$", ErrorMessage = "Please choose a language.")]
    public string Language { get; set; } = "bs";
}

public class ChangePasswordRequest
{
    [Required(ErrorMessage = "Please enter your current password.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a new password.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    public string NewPassword { get; set; } = string.Empty;
}

public class DeleteAccountRequest
{
    [Required(ErrorMessage = "Please enter your password.")]
    public string Password { get; set; } = string.Empty;
}

// ----- Responses (what the API sends back) -----

public record UserDto(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    string? DisplayName,
    string Role,
    string? Phone,
    string Language,
    string? AvatarUrl = null)
{
    public static UserDto From(User user) =>
        new(user.Id, user.Email, user.FirstName, user.LastName, user.DisplayName, user.Role, user.Phone, user.Language);
}

public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);
