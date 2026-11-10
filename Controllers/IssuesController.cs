using Microsoft.AspNetCore.Mvc;
using product_website.Data;
using product_website.Models;
using product_website.Services;

namespace product_website.Controllers;

/// <summary>The staff "Issues" pages: list, detail with actions, and the map.</summary>
public class IssuesController : StaffController
{
    private readonly StaffReportRepository _staffReports;
    private readonly ReportRepository _reports;
    private readonly PhotoStorage _photos;

    public IssuesController(
        UserRepository users, StaffReportRepository staffReports, ReportRepository reports, PhotoStorage photos)
        : base(users)
    {
        _staffReports = staffReports;
        _reports = reports;
        _photos = photos;
    }

    // ---------- List ----------

    // GET /Issues?status=open&categoryId=3&q=...&page=2
    public async Task<IActionResult> Index(ReportFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.Sort)) filter.Sort = "oldest";

        var scope = await GetScopeAsync();
        var model = await _staffReports.ListAsync(filter, scope, StaffId, IsAdmin);

        model.Categories = await _staffReports.GetCategoryOptionsAsync(scope);
        if (IsAdmin)
            model.Departments = await _staffReports.GetDepartmentOptionsAsync();
        else if (scope is int id)
            model.DepartmentName = await _staffReports.GetDepartmentNameAsync(id);

        return View(model);
    }

    // ---------- Map ----------

    // GET /Issues/Map
    public async Task<IActionResult> Map(ReportFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.Status)) filter.Status = "open";
        if (string.IsNullOrWhiteSpace(filter.Sort)) filter.Sort = "oldest";

        var scope = await GetScopeAsync();
        var model = new ReportListViewModel
        {
            Filter = filter,
            IsAdmin = IsAdmin,
            Categories = await _staffReports.GetCategoryOptionsAsync(scope)
        };
        if (IsAdmin) model.Departments = await _staffReports.GetDepartmentOptionsAsync();
        return View(model);
    }

    // GET /Issues/MapData?status=open&categoryId=3   (JSON for the map)
    public async Task<IActionResult> MapData(ReportFilter filter)
    {
        var scope = await GetScopeAsync();
        var points = await _staffReports.GetMapPointsAsync(filter, scope, StaffId, IsAdmin);
        return Json(points);
    }

    // ---------- Detail ----------

    // GET /Issues/Details/12
    public async Task<IActionResult> Details(int id)
    {
        var scope = await GetScopeAsync();
        var report = await _staffReports.GetDetailAsync(id);
        if (report is null || (scope.HasValue && report.DepartmentId != scope.Value))
            return NotFound();

        var model = new ReportDetailViewModel
        {
            Report = report,
            IsAdmin = IsAdmin,
            CurrentUserId = StaffId,
            Assignable = await _staffReports.GetAssignableAsync(report.DepartmentId)
        };
        if (IsAdmin)
        {
            model.Categories = await _staffReports.GetCategoryOptionsAsync();
            model.Departments = await _staffReports.GetDepartmentOptionsAsync();
        }
        return View(model);
    }

    /// <summary>Checks the report exists and the user may work on it (operators: own department only).</summary>
    private async Task<bool> CanAccessAsync(int id)
    {
        var departmentId = await _staffReports.GetReportDepartmentAsync(id);
        if (departmentId is null) return false;

        var scope = await GetScopeAsync();
        return !scope.HasValue || scope.Value == departmentId.Value;
    }

    private IActionResult BackToDetails(int id) => RedirectToAction(nameof(Details), new { id });

    // ---------- Actions on a report ----------

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int id, StatusForm form)
    {
        if (!await CanAccessAsync(id)) return NotFound();
        if (!ModelState.IsValid)
        {
            Failure("Please choose a valid status.");
            return BackToDetails(id);
        }

        var note = string.IsNullOrWhiteSpace(form.Note) ? null : form.Note.Trim();
        if (form.Status == "rejected" && note is null)
        {
            Failure("Please write the reason for rejecting this report. The citizen will see it.");
            return BackToDetails(id);
        }

        var scope = await GetScopeAsync();
        var result = await _reports.ChangeStatusAsync(id, form.Status, StaffId, scope, note, form.PublicComment);

        switch (result)
        {
            case StatusChangeResult.Ok:
                Success($"Status changed to \"{UiFormat.StatusLabel(form.Status)}\". The citizen has been notified.");
                break;
            case StatusChangeResult.Unchanged:
                Failure("The report already has this status.");
                break;
            case StatusChangeResult.Forbidden:
                return Forbid();
            default:
                return NotFound();
        }
        return BackToDetails(id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int id, AssignForm form)
    {
        if (!await CanAccessAsync(id)) return NotFound();

        if (form.AssigneeId == 0)
        {
            await _staffReports.UnassignAsync(id, StaffId);
            Success("The report is no longer assigned to anyone.");
            return BackToDetails(id);
        }

        var name = await _staffReports.AssignAsync(id, StaffId, form.AssigneeId);
        if (name is null)
        {
            Failure("That person cannot take this report. Choose an operator of this department or an admin.");
            return BackToDetails(id);
        }

        // Handing it to someone is the "assigned" step: move new reports forward automatically.
        var detail = await _staffReports.GetDetailAsync(id);
        if (detail is not null && detail.Status is "submitted" or "in_review")
        {
            var scope = await GetScopeAsync();
            await _reports.ChangeStatusAsync(id, "assigned", StaffId, scope, $"Assigned to {name}.", null);
        }

        Success($"Assigned to {name}.");
        return BackToDetails(id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPriority(int id, PriorityForm form)
    {
        if (!IsAdmin) return Forbid();
        if (!await CanAccessAsync(id)) return NotFound();
        if (!ModelState.IsValid)
        {
            Failure("Unknown priority.");
            return BackToDetails(id);
        }

        await _staffReports.SetPriorityAsync(id, StaffId, form.Priority);
        Success($"Priority set to {UiFormat.PriorityLabel(form.Priority).ToLowerInvariant()}.");
        return BackToDetails(id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reassign(int id, ReassignForm form)
    {
        if (!IsAdmin) return Forbid();
        if (!await CanAccessAsync(id)) return NotFound();

        if (await _staffReports.ReassignAsync(id, StaffId, form.CategoryId, form.DepartmentId))
            Success("Category and department updated.");
        else
            Failure("Could not move the report. Check the category and department.");

        return BackToDetails(id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(int id, CommentForm form)
    {
        if (!await CanAccessAsync(id)) return NotFound();
        if (!ModelState.IsValid)
        {
            Failure("Please write a comment (up to 1000 characters).");
            return BackToDetails(id);
        }

        await _staffReports.AddCommentAsync(id, StaffId, form.Body, form.IsInternal);
        Success(form.IsInternal
            ? "Internal note saved. Citizens cannot see it."
            : "Reply posted. The citizen has been notified.");
        return BackToDetails(id);
    }

    // POST /Issues/SaveAll/12   one button saves every change made on the detail page
    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> SaveAll(int id, SaveAllForm form, IFormFile? photo, CancellationToken ct)
    {
        var scope = await GetScopeAsync();
        var report = await _staffReports.GetDetailAsync(id);
        if (report is null || (scope.HasValue && report.DepartmentId != scope.Value))
            return NotFound();

        var note = string.IsNullOrWhiteSpace(form.Note) ? null : form.Note.Trim();
        var publicMessage = string.IsNullOrWhiteSpace(form.PublicComment) ? null : form.PublicComment.Trim();
        var commentBody = string.IsNullOrWhiteSpace(form.CommentBody) ? null : form.CommentBody.Trim();

        // ---- Check everything first, so nothing is half saved ----
        var newStatus = string.IsNullOrWhiteSpace(form.Status) ? report.Status : form.Status;
        if (!UiFormat.AllStatuses.Contains(newStatus))
        {
            Failure("Please choose a valid status.");
            return BackToDetails(id);
        }
        var statusChanges = newStatus != report.Status;

        if (statusChanges && newStatus == "rejected" && note is null)
        {
            Failure("Please write the reason for rejecting this report. The citizen will see it.");
            return BackToDetails(id);
        }
        if ((note?.Length ?? 0) > 500 || (publicMessage?.Length ?? 0) > 1000 || (commentBody?.Length ?? 0) > 1000)
        {
            Failure("A note or comment is too long.");
            return BackToDetails(id);
        }

        string? photoExtension = null;
        if (photo is not null && photo.Length > 0)
        {
            if (photo.Length > PhotoStorage.MaxBytes)
            {
                Failure("The photo must be smaller than 8 MB.");
                return BackToDetails(id);
            }
            if (await _staffReports.CountResolutionPhotosAsync(id) >= PhotoStorage.MaxPhotos)
            {
                Failure($"A report can have up to {PhotoStorage.MaxPhotos} \"after\" photos.");
                return BackToDetails(id);
            }
            photoExtension = await PhotoStorage.DetectExtensionAsync(photo, ct);
            if (photoExtension is null)
            {
                Failure("Only JPEG, PNG or WebP photos are allowed.");
                return BackToDetails(id);
            }
        }

        // ---- Apply ----
        var done = new List<string>();
        var problems = new List<string>();

        if (IsAdmin && form.CategoryId.HasValue && form.DepartmentId.HasValue
            && (form.CategoryId != report.CategoryId || form.DepartmentId != report.DepartmentId))
        {
            if (await _staffReports.ReassignAsync(id, StaffId, form.CategoryId.Value, form.DepartmentId.Value))
                done.Add("Category and department updated.");
            else
                problems.Add("Could not move the report. Check the category and department.");
        }

        if (IsAdmin && form.Priority is "low" or "normal" or "high" && form.Priority != report.Priority)
        {
            await _staffReports.SetPriorityAsync(id, StaffId, form.Priority!);
            done.Add($"Priority set to {UiFormat.PriorityLabel(form.Priority).ToLowerInvariant()}.");
        }

        if (form.AssigneeId.HasValue && form.AssigneeId.Value != (report.AssignedTo ?? 0))
        {
            if (form.AssigneeId.Value == 0)
            {
                await _staffReports.UnassignAsync(id, StaffId);
                done.Add("Assignee removed.");
            }
            else
            {
                var name = await _staffReports.AssignAsync(id, StaffId, form.AssigneeId.Value);
                if (name is null)
                    problems.Add("That person cannot take this report. Choose an operator of this department or an admin.");
                else
                {
                    done.Add($"Assigned to {name}.");
                    // Handing it to someone is the "assigned" step (unless the status is being set by hand).
                    if (!statusChanges && report.Status is "submitted" or "in_review")
                        await _reports.ChangeStatusAsync(id, "assigned", StaffId, scope, $"Assigned to {name}.", null);
                }
            }
        }

        if (statusChanges)
        {
            var result = await _reports.ChangeStatusAsync(id, newStatus, StaffId, scope, note, publicMessage);
            if (result == StatusChangeResult.Ok)
                done.Add($"Status changed to \"{UiFormat.StatusLabel(newStatus)}\". The citizen has been notified.");
            else if (result == StatusChangeResult.Forbidden)
                return Forbid();
            else
                problems.Add("The status could not be changed.");
        }
        else
        {
            // Status not changed: keep what was typed instead of losing it.
            if (publicMessage is not null)
            {
                await _staffReports.AddCommentAsync(id, StaffId, publicMessage, false);
                done.Add("Message to the citizen sent.");
            }
            if (note is not null)
            {
                await _staffReports.AddCommentAsync(id, StaffId, note, true);
                done.Add("Internal note saved.");
            }
        }

        if (commentBody is not null)
        {
            await _staffReports.AddCommentAsync(id, StaffId, commentBody, form.IsInternal);
            done.Add(form.IsInternal ? "Internal note saved." : "Reply posted. The citizen has been notified.");
        }

        if (photoExtension is not null && photo is not null)
        {
            var url = await _photos.SaveAsync(photo, photoExtension, ct, "resolutions");
            try
            {
                await _staffReports.AddResolutionPhotoAsync(id, StaffId, url);
                done.Add("\"After\" photo added.");
            }
            catch
            {
                _photos.Delete(url);
                throw;
            }
        }

        if (problems.Count > 0) Failure(string.Join(" ", problems));
        if (done.Count > 0) Success(string.Join(" ", done));
        else if (problems.Count == 0) Failure("Nothing to save: you did not change anything.");

        return BackToDetails(id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id)
    {
        if (!await CanAccessAsync(id)) return NotFound();

        if (await _staffReports.SetArchivedAsync(id, StaffId, true))
            Success("Report archived. It is hidden from the lists unless you choose \"Archived\".");
        else
            Failure("Only resolved or rejected reports can be archived.");
        return BackToDetails(id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unarchive(int id)
    {
        if (!await CanAccessAsync(id)) return NotFound();

        await _staffReports.SetArchivedAsync(id, StaffId, false);
        Success("Report restored.");
        return BackToDetails(id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> AddPhoto(int id, IFormFile? photo, CancellationToken ct)
    {
        if (!await CanAccessAsync(id)) return NotFound();

        if (photo is null || photo.Length == 0)
        {
            Failure("Please choose a photo.");
            return BackToDetails(id);
        }
        if (photo.Length > PhotoStorage.MaxBytes)
        {
            Failure("The photo must be smaller than 8 MB.");
            return BackToDetails(id);
        }
        if (await _staffReports.CountResolutionPhotosAsync(id) >= PhotoStorage.MaxPhotos)
        {
            Failure($"A report can have up to {PhotoStorage.MaxPhotos} \"after\" photos.");
            return BackToDetails(id);
        }

        var extension = await PhotoStorage.DetectExtensionAsync(photo, ct);
        if (extension is null)
        {
            Failure("Only JPEG, PNG or WebP photos are allowed.");
            return BackToDetails(id);
        }

        var url = await _photos.SaveAsync(photo, extension, ct, "resolutions");
        try
        {
            await _staffReports.AddResolutionPhotoAsync(id, StaffId, url);
        }
        catch
        {
            _photos.Delete(url);
            throw;
        }

        Success("\"After\" photo added. Citizens see it on the report.");
        return BackToDetails(id);
    }
}
