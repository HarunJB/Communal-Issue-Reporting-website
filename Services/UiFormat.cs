using System.Globalization;

namespace product_website.Services;

/// <summary>Small helpers the dashboard pages use for dates, statuses and labels.</summary>
public static class UiFormat
{
    public static readonly string[] OpenStatuses = { "submitted", "in_review", "assigned", "in_progress" };
    public static readonly string[] AllStatuses = { "submitted", "in_review", "assigned", "in_progress", "resolved", "rejected" };

    private static readonly TimeZoneInfo Zone = FindZone();

    private static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "Europe/Sarajevo", "Central European Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>Database times are UTC; show them in Sarajevo time.</summary>
    public static DateTime Local(DateTime time)
    {
        var utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
    }

    public static string Date(DateTime time) =>
        Local(time).ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string DateTimeText(DateTime time) =>
        Local(time).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"12 min", "5 h", "3 d" since the given time.</summary>
    public static string Age(DateTime time)
    {
        var span = DateTime.UtcNow - DateTime.SpecifyKind(time, DateTimeKind.Utc);
        if (span.TotalMinutes < 1) return "now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} h";
        return $"{(int)span.TotalDays} d";
    }

    public static int AgeDays(DateTime time) =>
        (int)(DateTime.UtcNow - DateTime.SpecifyKind(time, DateTimeKind.Utc)).TotalDays;

    public static string Hours(double? hours)
    {
        if (hours is null) return "-";
        if (hours < 1) return "< 1 h";
        if (hours < 48) return $"{hours.Value:0.#} h";
        return $"{hours.Value / 24:0.#} d";
    }

    public static string StatusLabel(string? status) => status switch
    {
        "submitted" => "Submitted",
        "in_review" => "In review",
        "assigned" => "Assigned",
        "in_progress" => "In progress",
        "resolved" => "Resolved",
        "rejected" => "Rejected",
        "open" => "Open",
        "all" => "All",
        _ => status ?? ""
    };

    /// <summary>A symbol next to the label, so status is never shown by color alone.</summary>
    public static string StatusGlyph(string? status) => status switch
    {
        "submitted" => "○",   // circle
        "in_review" => "◔",
        "assigned" => "◑",
        "in_progress" => "◕",
        "resolved" => "✓",    // check
        "rejected" => "✕",    // cross
        _ => "○"
    };

    public static string PriorityLabel(string? priority) => priority switch
    {
        "high" => "High",
        "low" => "Low",
        _ => "Normal"
    };

    public static string RoleLabel(string? role) => role switch
    {
        "admin" => "Admin",
        "operator" => "Operator",
        "citizen" => "Citizen",
        _ => role ?? ""
    };

    public static string Short(string? text, int length)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = text.Trim();
        return text.Length <= length ? text : text[..length].TrimEnd() + "...";
    }

    /// <summary>Readable text for an audit action such as "report.status_changed".</summary>
    public static string ActionLabel(string action) =>
        action.Replace('.', ' ').Replace('_', ' ');
}
