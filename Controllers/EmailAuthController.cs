using Microsoft.AspNetCore.Mvc;
using product_website.Data;
using product_website.Models;
using product_website.Services;

namespace product_website.Controllers;

/// <summary>Email verification and forgotten password (the app sends a 6 digit code by email).</summary>
[ApiController]
[Route("api/auth")]
public class EmailAuthController : ControllerBase
{
    private readonly UserRepository _users;
    private readonly EmailCodeRepository _codes;
    private readonly EmailSender _email;

    public EmailAuthController(UserRepository users, EmailCodeRepository codes, EmailSender email)
    {
        _users = users;
        _codes = codes;
        _email = email;
    }

    // POST /api/auth/verify-email
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request)
    {
        var user = await _users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is null) return BadRequest(new { message = "This code is wrong or has expired. Please ask for a new one." });

        var (ok, error) = await _codes.CheckAsync(user.Id, "verify", request.Code);
        if (!ok) return BadRequest(new { message = error });

        await _codes.MarkVerifiedAsync(user.Id);
        return Ok(new { message = "Email verified." });
    }

    // POST /api/auth/resend-code
    [HttpPost("resend-code")]
    public async Task<IActionResult> ResendCode(EmailOnlyRequest request)
    {
        var user = await _users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is not null && user.Status == "active" && !await _codes.IsVerifiedAsync(user.Id))
        {
            var code = await _codes.CreateAsync(user.Id, "verify");
            if (code is null)
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { message = "Please wait a minute before asking for another code." });
            await _email.SendCodeAsync(user.Email, user.FirstName, "verify", code);
        }
        // Same answer whether or not the account exists.
        return Ok(new { message = "If this account needs verification, we sent a new code." });
    }

    // POST /api/auth/forgot-password
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailOnlyRequest request)
    {
        var user = await _users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is not null && user.Status == "active")
        {
            var code = await _codes.CreateAsync(user.Id, "reset");
            if (code is not null)
                await _email.SendCodeAsync(user.Email, user.FirstName, "reset", code);
        }
        return Ok(new { message = "If an account with this email exists, we sent a code to it." });
    }

    // POST /api/auth/reset-password
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await _users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is null || user.Status != "active")
            return BadRequest(new { message = "This code is wrong or has expired. Please ask for a new one." });

        var (ok, error) = await _codes.CheckAsync(user.Id, "reset", request.Code);
        if (!ok) return BadRequest(new { message = error });

        await _users.UpdatePasswordAsync(user.Id, BCrypt.Net.BCrypt.HashPassword(request.NewPassword));
        await _codes.MarkVerifiedAsync(user.Id); // they proved they own the mailbox
        return Ok(new { message = "Password changed. You can now sign in." });
    }
}
