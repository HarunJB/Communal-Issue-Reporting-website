using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using product_website.Data;

namespace product_website.Controllers;

/// <summary>
/// Base class of every staff page (website, cookie login). Only operators and admins get in.
/// An operator only ever works with the reports of their own department.
/// </summary>
[Authorize(Roles = "operator,admin")]
public abstract class StaffController : Controller
{
    private readonly UserRepository _users;
    private int? _scope;
    private bool _scopeLoaded;

    protected StaffController(UserRepository users) => _users = users;

    protected int StaffId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    protected bool IsAdmin => User.IsInRole("admin");

    /// <summary>Department id for operators; null for admins (who see every department).</summary>
    protected async Task<int?> GetScopeAsync()
    {
        if (_scopeLoaded) return _scope;
        _scopeLoaded = true;

        if (IsAdmin)
        {
            _scope = null;
        }
        else
        {
            // Never null for an operator: if no department is set, they match no report at all.
            _scope = await _users.GetDepartmentIdAsync(StaffId) ?? -1;
        }
        return _scope;
    }

    protected void Success(string message) => TempData["Message"] = message;

    protected void Failure(string message) => TempData["Error"] = message;
}
