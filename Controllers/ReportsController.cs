using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using product_website.Data;
using product_website.Models;
using product_website.Services;

namespace product_website.Controllers;

[Route("api/reports")]
public class ReportsController : AuthedApiController
{
    private readonly ReportRepository _reports;
    private readonly CategoryRepository _categories;
    private readonly PhotoStorage _photos;
    private readonly ReputationRepository _reputation;

    public ReportsController(
        ReportRepository reports, CategoryRepository categories, PhotoStorage photos, ReputationRepository reputation)
    {
        _reports = reports;
        _categories = categories;
        _photos = photos;
        _reputation = reputation;
    }

    // GET /api/reports?categoryId=3&limit=100&offset=0   (public feed)
    [HttpGet]
    public async Task<IActionResult> Feed(
        [FromQuery] int? categoryId, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(offset, 0);
        return Ok(await _reports.GetFeedAsync(UserId, categoryId, limit, offset));
    }

    // GET /api/reports/mine
    [HttpGet("mine")]
    public async Task<IActionResult> Mine() => Ok(await _reports.GetMineAsync(UserId));

    // GET /api/reports/nearby?lat=43.85&lng=18.41&categoryId=3
    [HttpGet("nearby")]
    public async Task<IActionResult> Nearby(
        [FromQuery] double lat, [FromQuery] double lng, [FromQuery] int categoryId)
    {
        if (!Geo.IsInSarajevo(lat, lng)) return Ok(new List<ReportSummary>());
        return Ok(await _reports.GetNearbyAsync(UserId, lat, lng, categoryId, Geo.NearbyRadiusMeters, 5));
    }

    // GET /api/reports/12
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        var detail = await _reports.GetDetailAsync(id, UserId);
        if (detail is null) return NotFound(new { message = "Report not found." });

        // Rejected or archived reports are only visible to their author and to staff.
        var hidden = detail.Status == "rejected" || detail.ArchivedAt is not null;
        if (hidden && !detail.IsMine && !IsStaff)
            return NotFound(new { message = "Report not found." });

        return Ok(detail);
    }

    // POST /api/reports   (multipart form: categoryId, latitude, longitude, address, description, isAnonymous, photos)
    [HttpPost]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> Create([FromForm] CreateReportForm form, CancellationToken ct)
    {
        // Daily limit by trust level (staff are never limited).
        var limitMessage = await _reputation.CheckDailyLimitAsync(UserId, IsStaff);
        if (limitMessage is not null)
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = limitMessage });

        if (!TryParseCoordinate(form.Latitude, out var latitude) ||
            !TryParseCoordinate(form.Longitude, out var longitude))
            return BadRequest(new { message = "The location is not valid." });

        if (!Geo.IsInSarajevo(latitude, longitude))
            return BadRequest(new { message = "The location must be inside Sarajevo Canton." });

        if (form.Photos.Count < 1)
            return BadRequest(new { message = "Please add at least one photo." });
        if (form.Photos.Count > PhotoStorage.MaxPhotos)
            return BadRequest(new { message = $"You can add up to {PhotoStorage.MaxPhotos} photos." });

        var category = await _categories.GetActiveByIdAsync(form.CategoryId!.Value);
        if (category is null)
            return BadRequest(new { message = "This category is not available." });

        // Check every photo before anything is saved.
        var extensions = new List<string>();
        foreach (var file in form.Photos)
        {
            if (file.Length == 0 || file.Length > PhotoStorage.MaxBytes)
                return BadRequest(new { message = "Each photo must be smaller than 8 MB." });

            var extension = await PhotoStorage.DetectExtensionAsync(file, ct);
            if (extension is null)
                return BadRequest(new { message = "Only JPEG, PNG or WebP photos are allowed." });

            extensions.Add(extension);
        }

        var address = string.IsNullOrWhiteSpace(form.Address) ? null : form.Address.Trim();
        var description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();

        var savedUrls = new List<string>();
        try
        {
            for (var i = 0; i < form.Photos.Count; i++)
                savedUrls.Add(await _photos.SaveAsync(form.Photos[i], extensions[i], ct));

            var id = await _reports.CreateAsync(
                UserId, category, latitude, longitude, address, description, form.IsAnonymous, savedUrls);

            return StatusCode(StatusCodes.Status201Created, new { id });
        }
        catch
        {
            // Don't leave orphan files if the database insert failed.
            foreach (var url in savedUrls) _photos.Delete(url);
            throw;
        }
    }

    // POST /api/reports/12/support
    [HttpPost("{id:int}/support")]
    public async Task<IActionResult> Support(int id)
    {
        var report = await _reports.GetBasicAsync(id);
        if (report is null) return NotFound(new { message = "Report not found." });

        if (report.UserId == UserId)
            return BadRequest(new { message = "You can't support your own report." });

        if (report.ArchivedAt is not null || report.Status is "resolved" or "rejected")
            return BadRequest(new { message = "This report is already closed." });

        await _reports.AddSupportAsync(id, UserId);
        return Ok(await _reports.GetSupportStateAsync(id, UserId));
    }

    // DELETE /api/reports/12/support
    [HttpDelete("{id:int}/support")]
    public async Task<IActionResult> Unsupport(int id)
    {
        var report = await _reports.GetBasicAsync(id);
        if (report is null) return NotFound(new { message = "Report not found." });

        await _reports.RemoveSupportAsync(id, UserId);
        return Ok(await _reports.GetSupportStateAsync(id, UserId));
    }

    private static bool TryParseCoordinate(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && !double.IsNaN(value) && !double.IsInfinity(value);
}
