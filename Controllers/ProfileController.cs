using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using product_website.Data;
using product_website.Models;

namespace product_website.Controllers;

/// <summary>Staff can change their own password here (admin-created accounts start with a temporary one).</summary>
[Authorize]
public class ProfileController : Controller
{
    private readonly UserRepository _users;

    public ProfileController(UserRepository users) => _users = users;

    [HttpGet]
    public IActionResult Password() => View(new StaffPasswordForm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Password(StaffPasswordForm model)
    {
        if (!ModelState.IsValid) return View(model);

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Forbid();
        var user = await _users.GetByIdAsync(id);
        if (user is null) return Forbid();

        if (!BCrypt.Net.BCrypt.Verify(model.CurrentPassword, user.PasswordHash))
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "Your current password is not correct.");
            return View(model);
        }

        await _users.UpdatePasswordAsync(id, BCrypt.Net.BCrypt.HashPassword(model.NewPassword));
        TempData["Message"] = "Password changed.";
        return RedirectToAction(nameof(Password));
    }
}
