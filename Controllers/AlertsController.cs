using Microsoft.AspNetCore.Mvc;
using product_website.Data;
using product_website.Models;

namespace product_website.Controllers;

/// <summary>The badges in the staff header, and the lists behind them.</summary>
public class AlertsController : StaffController
{
    private readonly AlertRepository _alerts;

    public AlertsController(UserRepository users, AlertRepository alerts) : base(users) => _alerts = alerts;

    // GET /Alerts/Counts   (called by the page every 30 seconds)
    [HttpGet]
    public async Task<IActionResult> Counts()
    {
        Response.Headers.CacheControl = "no-store";
        return Json(await _alerts.GetCountsAsync(await GetScopeAsync()));
    }

    // GET /Alerts/Index?kind=unassigned|triage|stale
    [HttpGet]
    public async Task<IActionResult> Index(string? kind)
    {
        // Operators see "unassigned"; admins see "triage" and "stale".
        kind = IsAdmin
            ? (kind == "stale" ? "stale" : "triage")
            : "unassigned";

        var model = new AlertListViewModel { Kind = kind };
        switch (kind)
        {
            case "triage":
                model.Title = "Needs triage";
                model.Explanation = "Open reports nobody is handling and no operator can pick up: they are in General Municipal Services, or their department has no active operator. Move them to the right department or assign them to someone.";
                break;
            case "stale":
                model.Title = "Stale reports";
                model.Explanation = "Open reports that are unassigned for more than 24 hours, or have had no change for more than 7 days.";
                break;
            default:
                model.Title = "Unassigned reports";
                model.Explanation = "Open reports in your department that nobody has taken yet. Open one and choose \"Assign to me\".";
                break;
        }

        model.Items = await _alerts.ListAsync(kind, await GetScopeAsync());
        return View(model);
    }
}
