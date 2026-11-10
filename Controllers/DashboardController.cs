using Microsoft.AspNetCore.Mvc;
using product_website.Data;

namespace product_website.Controllers;

public class DashboardController : StaffController
{
    private readonly DashboardRepository _dashboard;
    private readonly StaffReportRepository _reports;

    public DashboardController(UserRepository users, DashboardRepository dashboard, StaffReportRepository reports)
        : base(users)
    {
        _dashboard = dashboard;
        _reports = reports;
    }

    // GET /Dashboard
    public async Task<IActionResult> Index()
    {
        var scope = await GetScopeAsync();
        var model = await _dashboard.GetAsync(scope, IsAdmin);

        model.ScopeName = scope is int id
            ? (await _reports.GetDepartmentNameAsync(id) ?? "Your department")
            : "All departments";

        return View(model);
    }
}
