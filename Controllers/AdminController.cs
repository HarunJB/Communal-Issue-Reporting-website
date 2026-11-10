using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using product_website.Data;
using product_website.Models;

namespace product_website.Controllers;

/// <summary>Admin only: users, departments, categories and the audit log.</summary>
[Authorize(Roles = "admin")]
public class AdminController : StaffController
{
    private readonly AdminRepository _admin;
    private readonly StaffReportRepository _staffReports;

    public AdminController(UserRepository users, AdminRepository admin, StaffReportRepository staffReports)
        : base(users)
    {
        _admin = admin;
        _staffReports = staffReports;
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

        return View("UserForm", new UserFormModel
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Role = user.Role,
            DepartmentId = user.DepartmentId,
            IsSelf = user.Id == StaffId,
            Departments = await _staffReports.GetDepartmentOptionsAsync()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UserEdit(UserFormModel model)
    {
        var existing = await _admin.GetUserAsync(model.Id);
        if (existing is null || existing.IsDeleted) return NotFound();

        model.Departments = await _staffReports.GetDepartmentOptionsAsync();
        model.IsSelf = model.Id == StaffId;

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
