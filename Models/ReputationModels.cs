namespace product_website.Models;

/// <summary>One line of a user's score history (the "ledger").</summary>
public class ReputationEventRow
{
    public int Id { get; set; }
    public int? ReportId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int Delta { get; set; }
    public int ScoreAfter { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReversedAt { get; set; }
}

/// <summary>Everything the app and the staff pages show about one user's score.</summary>
public class ReputationSummary
{
    public int Score { get; set; }
    public string Level { get; set; } = "neighbor";
    public string LevelLabel { get; set; } = "Neighbor";
    public int LevelMin { get; set; }
    public string? NextLevelLabel { get; set; }
    public int? NextLevelMin { get; set; }
    public int DailyLimit { get; set; }
    public int ReportsToday { get; set; }
    public int Confirmed { get; set; }
    public int FalseReports { get; set; }
}

/// <summary>What one staff decision did to the reporter's score (null when nothing changed).</summary>
public class VerdictOutcome
{
    public int NetDelta { get; set; }
    public int ScoreAfter { get; set; }
    public string LevelLabel { get; set; } = string.Empty;
    public bool LevelChanged { get; set; }
}

/// <summary>JSON for GET /api/me/reputation (the app's trust card and history).</summary>
public class ReputationResponse
{
    public ReputationSummary Summary { get; set; } = new();
    public List<ReputationEventDto> Events { get; set; } = new();

    /// <summary>False for Restricted/Neighbor: badges are a reward for Trusted (70+) and Guardian.</summary>
    public bool BadgesUnlocked { get; set; }
    public string BadgesHint { get; set; } = string.Empty;
    public List<BadgeDto> Badges { get; set; } = new();
}

public class BadgeDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Earned { get; set; }
}

public class ReputationEventDto
{
    public int? ReportId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Delta { get; set; }
    public int ScoreAfter { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
