using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using product_website.Data;
using product_website.Models;
using product_website.Services;

namespace product_website.Controllers;

/// <summary>Admin only: users, departments, categories and the audit log.</summary>
[Authorize(Roles = "admin")]
public class AdminController : StaffController
{
    private readonly AdminRepository _admin;
    private readonly StaffReportRepository _staffReports;
    private readonly ReputationRepository _reputation;

    public AdminController(
        UserRepository users, AdminRepository admin, StaffReportRepository staffReports, ReputationRepository reputation)
        : base(users)
    {
        _admin = admin;
        _staffReports = staffReports;
        _reputation = reputation;
    }

    // =================================================================
    // Users
    // =================================================================

    // GET /Admin/Users
    public async Task<IActionResult> Users(string? q, string? role, string? status, int page = 1)
    {
        var model = await _admin.ListUsersAsync(q, role, status, page);
        model.CurrentUserId = StaffId;
        return View(model);
    }

    // GET /Admin/UserCreate
    [HttpGet]
    public async Task<IActionResult> UserCreate()
    {
        return View("UserForm", new UserFormModel { Departments = await _staffReports.GetDepartmentOptionsAsync() });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UserCreate(UserFormModel model)
    {
        model.Departments = await _staffReports.GetDepartmentOptionsAsync();
        await ValidateUserFormAsync(model, creating: true);
        if (!ModelState.IsValid) return View("UserForm", model);

        await _admin.CreateUserAsync(StaffId, model, BCrypt.Net.BCrypt.HashPassword(model.Password!));
        Success($"Account created for {model.Email.Trim().ToLowerInvariant()}. Share the password with them securely.");
        return RedirectToAction(nameof(Users));
    }

    // GET /Admin/UserEdit/5
    [HttpGet]
    public async Task<IActionResult> UserEdit(int id)
    {
        var user = await _admin.GetUserAsync(id);
        if (user is null || user.IsDeleted) return NotFound();

        var model = new UserFormModel
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Role = user.Role,
            DepartmentId = user.DepartmentId,
            IsSelf = user.Id == StaffId,
            Departments = await _staffReports.GetDepartmentOptionsAsync()
        };
        await LoadReputationAsync(model, user.Role);
        return View("UserForm", model);
    }

    /// <summary>Fills the trust score panel on the edit page (citizens only).</summary>
    private async Task LoadReputationAsync(UserFormModel model, string role)
    {
        if (model.Id == 0 || role != "citizen") return;
        model.Reputation = await _reputation.GetSummaryAsync(model.Id);
        model.ReputationEvents = await _reputation.GetEventsAsync(model.Id, 30);
    }

    // POST /Admin/AdjustScore  (add or remove trust points by hand; the reason is kept and the citizen is told)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AdjustScore(int id, int delta, string? note)
    {
        var user = await _admin.GetUserAsync(id);
        if (user is null || user.IsDeleted) return NotFound();

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (user.Role != "citizen")
            Failure("Only citizens have a trust score.");
        else if (delta == 0 || delta < -100 || delta > 100)
            Failure("Enter a number of points between -100 and 100 (not 0).");
        else if (note is null || note.Length < 3)
            Failure("Please write a short reason. The citizen will see it.");
        else if (note.Length > 300)
            Failure("The reason is too long (up to 300 characters).");
        else
        {
            var after = await _reputation.AdjustAsync(StaffId, id, delta, note);
            if (after is null) Failure("Could not change the score.");
            else Success($"Trust score of {user.FullName} is now {after} ({ReputationRules.LevelLabel(after.Value)}).");
        }

        return RedirectToAction(nameof(UserEdit), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UserEdit(UserFormModel model)
    {
        var existing = await _admin.GetUserAsync(model.Id);
        if (existing is null || existing.IsDeleted) return NotFound();

        model.Departments = await _staffReports.GetDepartmentOptionsAsync();
        model.IsSelf = model.Id == StaffId;
        await LoadReputationAsync(model, existing.Role);

        // You cannot take admin rights away from yourself (you could lock everyone out).
        if (model.IsSelf && model.Role != "admin")
            ModelState.AddModelError(nameof(model.Role), "You cannot remove your own admin role.");
        else if (existing.Role == "admin" && model.Role != "admin"
                 && !await _admin.AnotherActiveAdminExistsAsync(model.Id))
            ModelState.AddModelError(nameof(model.Role), "There must be at least one admin.");

        await ValidateUserFormAsync(model, creating: false);
        if (!ModelState.IsValid) return View("UserForm", model);

        var hash = string.IsNullOrWhiteSpace(model.Password) ? null : BCrypt.Net.BCrypt.HashPassword(model.Password);
        await _admin.UpdateUserAsync(StaffId, model, hash);
        Success("Account updated.");
        return RedirectToAction(nameof(Users));
    }

    private async Task ValidateUserFormAsync(UserFormModel model, bool creating)
    {
        if (model.Role == "operator" && model.DepartmentId is null)
            ModelState.AddModelError(nameof(model.DepartmentId), "An operator must belong to a department.");

        if (creating && string.IsNullOrWhiteSpace(model.Password))
            ModelState.AddModelError(nameof(model.Password), "Please set a password.");

        if (!string.IsNullOrWhiteSpace(model.Password) && model.Password.Length < 8)
            ModelState.AddModelError(nameof(model.Password), "Password must be at least 8 characters.");

        if (!string.IsNullOrWhiteSpace(model.Email)
            && await _admin.EmailTakenAsync(model.Email.Trim().ToLowerInvariant(), model.Id))
            ModelState.AddModelError(nameof(model.Email), "Another account already uses this email.");
    }

    // POST /Admin/UserStatus  (suspend or reactivate)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UserStatus(int id, string status)
    {
        if (status is not ("active" or "suspended")) return BadRequest();

        var user = await _admin.GetUserAsync(id);
        if (user is null || user.IsDeleted) return NotFound();

        if (id == StaffId)
        {
            Failure("You cannot suspend your own account.");
            return RedirectToAction(nameof(Users));
        }
        if (status == "suspended" && user.Role == "admin" && !await _admin.AnotherActiveAdminExistsAsync(id))
        {
            Failure("There must be at least one active admin.");
            return RedirectToAction(nameof(Users));
        }

        await _admin.SetUserStatusAsync(StaffId, id, status);
        Success(status == "suspended"
            ? $"{user.FullName} is suspended and can no longer sign in."
            : $"{user.FullName} can sign in again.");
        return RedirectToAction(nameof(Users));
    }

    // =================================================================
    // Departments
    // =================================================================

    public async Task<IActionResult> Departments() => View(await _admin.ListDepartmentsAsync());

    [HttpGet]
    public async Task<IActionResult> DepartmentEdit(int id = 0)
    {
        if (id == 0) return View(new DepartmentFormModel());

        var model = await _admin.GetDepartmentAsync(id);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DepartmentEdit(DepartmentFormModel model)
    {
        if (!ModelState.IsValid) return View(model);

        await _admin.SaveDepartmentAsync(StaffId, model);
        Success(model.Id == 0 ? "Department created." : "Department updated.");
        return RedirectToAction(nameof(Departments));
    }

    // =================================================================
    // Categories
    // =================================================================

    public async Task<IActionResult> Categories() => View(await _admin.ListCategoriesAsync());

    [HttpGet]
    public async Task<IActionResult> CategoryEdit(int id = 0)
    {
        var departments = await _staffReports.GetDepartmentOptionsAsync();
        if (id == 0) return View(new CategoryFormModel { Departments = departments });

        var model = await _admin.GetCategoryAsync(id);
        if (model is null) return NotFound();
        model.Departments = departments;
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CategoryEdit(CategoryFormModel model)
    {
        model.Departments = await _staffReports.GetDepartmentOptionsAsync();
        if (!ModelState.IsValid) return View(model);

        await _admin.SaveCategoryAsync(StaffId, model);
        Success(model.Id == 0 ? "Category created." : "Category updated.");
        return RedirectToAction(nameof(Categories));
    }

    // =================================================================
    // Audit log
    // =================================================================

    public async Task<IActionResult> Audit(string? actionFilter, string? entityType, int page = 1) =>
        View(await _admin.GetAuditAsync(actionFilter, entityType, page));
}
