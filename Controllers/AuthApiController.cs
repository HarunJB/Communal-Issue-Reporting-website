using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using product_website.Data;
using product_website.Models;
using product_website.Services;

namespace product_website.Controllers;

/// <summary>JSON endpoints used by the Flutter app. The website keeps using AccountController.</summary>
[ApiController]
[Route("api/auth")]
public class AuthApiController : ControllerBase
{
    private readonly UserRepository _users;
    private readonly TokenService _tokens;
    private readonly EmailCodeRepository _codes;
    private readonly EmailSender _email;

    public AuthApiController(UserRepository users, TokenService tokens, EmailCodeRepository codes, EmailSender email)
    {
        _users = users;
        _tokens = tokens;
        _codes = codes;
        _email = email;
    }

    // POST /api/auth/register
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _users.EmailExistsAsync(email))
            return Conflict(new { message = "An account with this email already exists." });

        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        int newId;
        try
        {
            newId = await _users.CreateCitizenAsync(
                email,
                BCrypt.Net.BCrypt.HashPassword(request.Password),
                request.FirstName.Trim(),
                request.LastName.Trim(),
                phone,
                request.Language);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        // New accounts must confirm their email with a 6 digit code.
        await _codes.MarkUnverifiedAsync(newId);
        var code = await _codes.CreateAsync(newId, "verify");
        var sent = code is not null && await _email.SendCodeAsync(email, request.FirstName.Trim(), "verify", code);

        return StatusCode(StatusCodes.Status201Created, new
        {
            message = sent ? "Account created. We sent a code to your email." : "Account created, but the email could not be sent. Ask for a new code.",
            requiresVerification = true
        });
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());

        // Same message for "no such user" and "wrong password" so emails can't be probed.
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        if (user.Status != "active")
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This account has been suspended." });

        if (user.Role == "citizen" && !await _codes.IsVerifiedAsync(user.Id))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Please verify your email address first.", code = "email_not_verified" });

        var (token, expiresAt) = _tokens.Create(user);
        return Ok(new AuthResponse(token, expiresAt, UserDto.From(user)));
    }

    // GET /api/auth/me   (needs "Authorization: Bearer <token>")
    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Me()
    {
        if (!int.TryParse(User.FindFirstValue("sub"), out var id))
            return Unauthorized();

        var user = await _users.GetByIdAsync(id);
        if (user is null || user.Status != "active")
            return Unauthorized();

        return Ok(UserDto.From(user));
    }
}
