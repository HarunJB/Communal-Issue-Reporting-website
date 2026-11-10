using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace product_website.Models;

// ----- Categories -----

public class CategoryDto
{
    public int Id { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string NameBs { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentNameEn { get; set; } = string.Empty;
    public string DepartmentNameBs { get; set; } = string.Empty;
    public string DefaultPriority { get; set; } = "normal";
}

// ----- Reports (what the app receives) -----

/// <summary>One row in the lists (home feed, my reports, similar reports) and the map.</summary>
public class ReportSummary
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryNameEn { get; set; } = string.Empty;
    public string CategoryNameBs { get; set; } = string.Empty;
    public string? CategoryIcon { get; set; }
    public string Status { get; set; } = "submitted";
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsAnonymous { get; set; }
    public bool IsMine { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int SupportCount { get; set; }
    public bool SupportedByMe { get; set; }
}

public class PhotoDto
{
    public string Url { get; set; } = string.Empty;
    /// <summary>'report' (taken by the citizen) or 'resolution' (staff "after" photo)</summary>
    public string Kind { get; set; } = "report";
}

public class HistoryDto
{
    public string? OldStatus { get; set; }
    public string NewStatus { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CommentDto
{
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorRole { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ReportDetail : ReportSummary
{
    // Used only for access checks. Never sent to the app (it would reveal who sent an anonymous report).
    [JsonIgnore] public int UserId { get; set; }
    [JsonIgnore] public DateTime? ArchivedAt { get; set; }

    public string DepartmentNameEn { get; set; } = string.Empty;
    public string DepartmentNameBs { get; set; } = string.Empty;

    /// <summary>Null when the report is anonymous.</summary>
    public string? AuthorName { get; set; }

    public List<PhotoDto> Photos { get; set; } = new();
    public List<HistoryDto> History { get; set; } = new();
    public List<CommentDto> Comments { get; set; } = new();
}

public class ReportBasic
{
    public int UserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? ArchivedAt { get; set; }
}

public class SupportResult
{
    public int SupportCount { get; set; }
    public bool SupportedByMe { get; set; }
}

// ----- Create a report (multipart form sent by the app) -----

public class CreateReportForm
{
    [Required(ErrorMessage = "Please choose a category.")]
    public int? CategoryId { get; set; }

    // Sent as text and parsed with "." decimals, so a Bosnian-language PC (which uses ",") can't misread them.
    [Required(ErrorMessage = "Please choose the location on the map.")]
    public string? Latitude { get; set; }

    [Required(ErrorMessage = "Please choose the location on the map.")]
    public string? Longitude { get; set; }

    [StringLength(255)]
    public string? Address { get; set; }

    [StringLength(1000, ErrorMessage = "The description is too long (1000 characters at most).")]
    public string? Description { get; set; }

    public bool IsAnonymous { get; set; }

    public List<IFormFile> Photos { get; set; } = new();
}

// ----- Staff -----

public class ChangeStatusRequest
{
    [Required]
    [RegularExpression("^(submitted|in_review|assigned|in_progress|resolved|rejected)$",
        ErrorMessage = "Unknown status.")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Shown to the citizen in the report's history (and as the rejection reason).</summary>
    [StringLength(500)]
    public string? Note { get; set; }

    /// <summary>Optional public response shown on the report.</summary>
    [StringLength(1000)]
    public string? PublicComment { get; set; }
}

// ----- Notices -----

public class NoticeDto
{
    public int Id { get; set; }
    public int? ReportId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
