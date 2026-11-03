using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using product_website.Data;
using product_website.Models;

namespace product_website.Controllers;

public class AccountController : Controller
{
    private readonly UserRepository _users;

    public AccountController(UserRepository users) => _users = users;

    // ---------- LOGIN ----------

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction(nameof(Success));

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        var user = await _users.GetByEmailAsync(model.Email.Trim().ToLowerInvariant());

        // Same message for "no such user" and "wrong password" so emails can't be probed.
        if (user is null || !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        if (user.Status != "active")
        {
            ModelState.AddModelError(string.Empty, "This account has been suspended.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName ?? user.FirstName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = model.RememberMe });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToAction(nameof(Success));
    }

    // ---------- REGISTER ----------

    [HttpGet, AllowAnonymous]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction(nameof(Success));

        return View(new RegisterViewModel());
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var email = model.Email.Trim().ToLowerInvariant();

        if (await _users.EmailExistsAsync(email))
        {
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
            return View(model);
        }

        var phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();

        try
        {
            await _users.CreateCitizenAsync(
                email,
                BCrypt.Net.BCrypt.HashPassword(model.Password),
                model.FirstName.Trim(),
                model.LastName.Trim(),
                phone,
                model.Language);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Two people registering the same email at the same moment.
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
            return View(model);
        }

        TempData["Success"] = "Account created. You can now sign in.";
        return RedirectToAction(nameof(Login));
    }

    // ---------- AFTER LOGIN ----------

    [Authorize]
    public IActionResult Success()
    {
        var model = new SuccessViewModel
        {
            Name = User.Identity?.Name ?? string.Empty,
            Email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            Role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty
        };
        return View(model);
    }

    // ---------- LOGOUT ----------

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
