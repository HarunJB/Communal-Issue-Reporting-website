using Microsoft.AspNetCore.Mvc;
using product_website.Data;
using product_website.Models;

namespace product_website.Controllers;

/// <summary>
/// Staff-only endpoint to change a report's status. The real staff UI is the web dashboard; this endpoint
/// lets you test statuses and notices now (for example from the Swagger-less terminal with curl).
/// </summary>
[Route("api/staff/reports")]
public class StaffReportsController : AuthedApiController
{
    private readonly ReportRepository _reports;
    private readonly UserRepository _users;

    public StaffReportsController(ReportRepository reports, UserRepository users)
    {
        _reports = reports;
        _users = users;
    }

    // POST /api/staff/reports/12/status   body: { "status": "in_progress", "note": "...", "publicComment": "..." }
    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] ChangeStatusRequest request)
    {
        if (!IsStaff) return Forbid();

        // Operators are limited to their own department; admins (null) may change any report.
        int? departmentId = null;
        if (UserRole == "operator")
        {
            departmentId = await _users.GetDepartmentIdAsync(UserId);
            if (departmentId is null) return Forbid();
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var comment = string.IsNullOrWhiteSpace(request.PublicComment) ? null : request.PublicComment.Trim();

        var result = await _reports.ChangeStatusAsync(id, request.Status, UserId, departmentId, note, comment);

        return result switch
        {
            StatusChangeResult.Ok => NoContent(),
            StatusChangeResult.NotFound => NotFound(new { message = "Report not found." }),
            StatusChangeResult.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                new { message = "This report belongs to another department." }),
            _ => BadRequest(new { message = "The report already has this status." }),
        };
    }
}
