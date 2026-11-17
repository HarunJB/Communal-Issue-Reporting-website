namespace product_website.Models;

/// <summary>The numbers shown in the small badges of the staff header.</summary>
public class AlertCounts
{
    /// <summary>Open reports nobody is handling (operators see their department only).</summary>
    public int Unassigned { get; set; }

    /// <summary>Unassigned reports that no operator can pick up: General services, or a department without operators.</summary>
    public int NeedsTriage { get; set; }

    /// <summary>Unassigned for more than 24 hours, or open with no change for more than 7 days.</summary>
    public int Stale { get; set; }

    /// <summary>All open reports in scope. Used only so the dashboard page knows when to reload itself.</summary>
    public int Open { get; set; }
}

public class AlertListItem
{
    public int Id { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string? AssignedName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AlertListViewModel
{
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public List<AlertListItem> Items { get; set; } = new();
}
