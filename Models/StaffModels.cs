using System.ComponentModel.DataAnnotations;

namespace product_website.Models;

// =====================================================================
// Models used by the staff dashboard (operators and admins)
// =====================================================================

public class Option
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

// ----- Report list -----

/// <summary>Filters of the report list. Bound from the query string, so every filtered view has a link.</summary>
public class ReportFilter
{
    public string? Q { get; set; }

    /// <summary>"open" (default: not resolved or rejected), "all", or one status.</summary>
    public string Status { get; set; } = "open";

    public int? CategoryId { get; set; }
    public int? DepartmentId { get; set; }

    /// <summary>"low", "normal" or "high". Only admins can filter by priority.</summary>
    public string? Priority { get; set; }

    /// <summary>"me" or "none" (unassigned).</summary>
    public string? Assigned { get; set; }

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>Show archived reports instead of active ones.</summary>
    public bool Archived { get; set; }

    /// <summary>"oldest" (default), "newest", "priority" (admins) or "supports".</summary>
    public string Sort { get; set; } = "oldest";

    public int Page { get; set; } = 1;
}

public class ReportListItem
{
    public int Id { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? CategoryIcon { get; set; }
    public string Status { get; set; } = "submitted";
    public string Priority { get; set; } = "normal";
    public string? Address { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsArchived { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string? AssignedName { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int SupportCount { get; set; }
}

public class ReportListViewModel
{
    public ReportFilter Filter { get; set; } = new();
    public List<ReportListItem> Items { get; set; } = new();
    public Dictionary<string, int> Counts { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public List<Option> Categories { get; set; } = new();
    public List<Option> Departments { get; set; } = new();
    public bool IsAdmin { get; set; }
    public string? DepartmentName { get; set; }
}

public class MapPoint
{
    public int Id { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Address { get; set; }
}

// ----- Report detail -----

public class StaffHistoryItem
{
    public string? OldStatus { get; set; }
    public string NewStatus { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string ChangedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class StaffCommentItem
{
    public int Id { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorRole { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class StaffPhotoItem
{
    public string Url { get; set; } = string.Empty;
    public string Kind { get; set; } = "report";
}

public class StaffReportDetail
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string Status { get; set; } = "submitted";
    public string Priority { get; set; } = "normal";
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Description { get; set; }
    public bool IsAnonymous { get; set; }
    public string Source { get; set; } = "mobile";
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public int? AssignedTo { get; set; }
    public string? AssignedName { get; set; }
    public int AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorEmail { get; set; }
    public string? AuthorPhone { get; set; }
    public int SupportCount { get; set; }

    public List<StaffPhotoItem> Photos { get; set; } = new();
    public List<StaffHistoryItem> History { get; set; } = new();
    public List<StaffCommentItem> Comments { get; set; } = new();
}

public class StaffOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class ReportDetailViewModel
{
    public StaffReportDetail Report { get; set; } = new();
    public List<StaffOption> Assignable { get; set; } = new();
    public List<Option> Categories { get; set; } = new();
    public List<Option> Departments { get; set; } = new();
    public bool IsAdmin { get; set; }
    public int CurrentUserId { get; set; }
}

// ----- Forms on the detail page -----

public class StatusForm
{
    [Required, RegularExpression("^(submitted|in_review|assigned|in_progress|resolved|rejected)$")]
    public string Status { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Note { get; set; }

    [StringLength(1000)]
    public string? PublicComment { get; set; }
}

public class AssignForm
{
    public int AssigneeId { get; set; }
}

public class PriorityForm
{
    [Required, RegularExpression("^(low|normal|high)$")]
    public string Priority { get; set; } = "normal";
}

public class ReassignForm
{
    public int CategoryId { get; set; }
    public int DepartmentId { get; set; }
}

public class CommentForm
{
    [Required(ErrorMessage = "Please write a comment."), StringLength(1000)]
    public string Body { get; set; } = string.Empty;

    public bool IsInternal { get; set; }
}

/// <summary>Everything on the detail page that can be saved together with one button.</summary>
public class SaveAllForm
{
    public string? Status { get; set; }
    public string? Note { get; set; }
    public string? PublicComment { get; set; }

    /// <summary>0 = nobody; null = field not sent.</summary>
    public int? AssigneeId { get; set; }

    public string? Priority { get; set; }
    public int? CategoryId { get; set; }
    public int? DepartmentId { get; set; }

    public string? CommentBody { get; set; }
    public bool IsInternal { get; set; }
}

// ----- Dashboard -----

public class DashboardKpis
{
    public int Open { get; set; }
    public int Unassigned { get; set; }
    public int NewToday { get; set; }
    public int ResolvedLast7Days { get; set; }
    public double? AvgResolutionHours { get; set; }
    public int OlderThan7Days { get; set; }
}

public class CountRow
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class DayRow
{
    public DateTime Day { get; set; }
    public int Created { get; set; }
    public int Resolved { get; set; }
}

public class DepartmentStatRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Open { get; set; }
    public int Resolved { get; set; }
    public double? AvgResolutionHours { get; set; }
}

public class DashboardViewModel
{
    public bool IsAdmin { get; set; }
    public string ScopeName { get; set; } = "All departments";
    public DashboardKpis Kpis { get; set; } = new();
    public Dictionary<string, int> StatusCounts { get; set; } = new();
    public List<CountRow> OpenByCategory { get; set; } = new();
    public List<DayRow> Days { get; set; } = new();
    public List<ReportListItem> NeedsAttention { get; set; } = new();
    public List<DepartmentStatRow> Departments { get; set; } = new();
}

// ----- Admin: users -----

public class UserRow
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = "citizen";
    public string Status { get; set; } = "active";
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ReportCount { get; set; }
    public bool IsDeleted { get; set; }
    public string FullName => $"{FirstName} {LastName}".Trim();
}

public class UserListViewModel
{
    public List<UserRow> Users { get; set; } = new();
    public string? Q { get; set; }
    public string? Role { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int Total { get; set; }
    public int CurrentUserId { get; set; }
}

/// <summary>Create a staff account, or edit role/department/status of an existing user.</summary>
public class UserFormModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "First name is required."), StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required."), StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required."), EmailAddress(ErrorMessage = "Please enter a valid email address."), StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression("^(citizen|operator|admin)$", ErrorMessage = "Please choose a role.")]
    public string Role { get; set; } = "operator";

    public int? DepartmentId { get; set; }

    /// <summary>Required when creating an account. When editing, leave empty to keep the password.</summary>
    [StringLength(100)]
    public string? Password { get; set; }

    public List<Option> Departments { get; set; } = new();
    public bool IsSelf { get; set; }
}

// ----- Admin: categories and departments -----

public class CategoryRow
{
    public int Id { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string NameBs { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string DefaultPriority { get; set; } = "normal";
    public bool IsActive { get; set; }
    public int ReportCount { get; set; }
}

public class CategoryFormModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "English name is required."), StringLength(100)]
    public string NameEn { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bosnian name is required."), StringLength(100)]
    public string NameBs { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Icon { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Please choose a department.")]
    public int DepartmentId { get; set; }

    [Required, RegularExpression("^(low|normal|high)$")]
    public string DefaultPriority { get; set; } = "normal";

    public bool IsActive { get; set; } = true;

    public List<Option> Departments { get; set; } = new();
}

public class DepartmentRow
{
    public int Id { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string NameBs { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public bool IsActive { get; set; }
    public int OperatorCount { get; set; }
    public int CategoryCount { get; set; }
    public int OpenReports { get; set; }
}

public class DepartmentFormModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "English name is required."), StringLength(150)]
    public string NameEn { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bosnian name is required."), StringLength(150)]
    public string NameBs { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Please enter a valid email address."), StringLength(255)]
    public string? ContactEmail { get; set; }

    public bool IsActive { get; set; } = true;
}

// ----- Admin: audit log -----

public class AuditRow
{
    public int Id { get; set; }
    public string? ActorName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuditViewModel
{
    public List<AuditRow> Rows { get; set; } = new();
    public string? Action { get; set; }
    public string? EntityType { get; set; }
    public List<string> Actions { get; set; } = new();
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int Total { get; set; }
}

// ----- Profile -----

public class StaffPasswordForm
{
    [Required(ErrorMessage = "Please enter your current password.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a new password."), StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please repeat the new password."), Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

// ----- Shared by pages with page numbers -----

public class PagerModel
{
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int Total { get; set; }
    public int PageSize { get; set; }
    public string Noun { get; set; } = "results";

    /// <summary>Builds the link of another page, keeping the current filters.</summary>
    public Func<int, string> LinkFor { get; set; } = _ => "#";
}
