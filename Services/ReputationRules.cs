namespace product_website.Services;

/// <summary>
/// All the numbers of the trust score in ONE place. Change them here and nowhere else.
///
///   +ConfirmedPoints  when staff accept a report as real (status assigned, in progress or resolved)
///   -FalsePenalty     when staff reject it as "the problem does not exist" (reason type "not_found")
///   0                 for duplicates, "not our responsibility" and other rejections
///
/// The score always stays between 0 and 100 and starts at StartScore.
/// </summary>
public static class ReputationRules
{
    public const int StartScore = 50;
    public const int MinScore = 0;
    public const int MaxScore = 100;

    public const int ConfirmedPoints = 10;
    public const int FalsePenalty = 15;

    public sealed record Level(string Key, string Label, int Min, int DailyReports);

    // Lowest level first. A user is in the highest level whose Min they reach.
    public static readonly Level[] Levels =
    {
        new("restricted", "Restricted", 0, 3),
        new("neighbor", "Neighbor", 20, 10),
        new("trusted", "Trusted", 70, 20),
        new("guardian", "Guardian", 90, 30),
    };

    public static int Clamp(int score) => Math.Max(MinScore, Math.Min(MaxScore, score));

    public static Level LevelFor(int score)
    {
        var level = Levels[0];
        foreach (var candidate in Levels)
            if (score >= candidate.Min) level = candidate;
        return level;
    }

    public static Level? NextLevel(int score)
    {
        foreach (var candidate in Levels)
            if (candidate.Min > score) return candidate;
        return null;
    }

    public static string LevelLabel(int score) => LevelFor(score).Label;

    /// <summary>The website chip class that matches the level (the colours already exist in dashboard.css).</summary>
    public static string ChipClass(int score) => LevelFor(score).Key switch
    {
        "restricted" => "chip-rejected",
        "neighbor" => "chip-muted",
        "trusted" => "chip-assigned",
        _ => "chip-in_progress"
    };

    public static readonly string[] RejectionCodes = { "not_found", "duplicate", "out_of_scope", "other" };

    public static string RejectionCodeLabel(string? code) => code switch
    {
        "not_found" => "The problem does not exist or was not found at the location",
        "duplicate" => "Duplicate of another report",
        "out_of_scope" => "Not a municipal responsibility",
        "other" => "Other reason",
        _ => ""
    };

    public static string KindLabel(string kind) => kind switch
    {
        "confirmed" => "Report confirmed",
        "false_report" => "Report rejected: problem not found",
        "reversal" => "Decision changed",
        "admin_adjust" => "Adjusted by staff",
        _ => kind
    };

    // ---- Badges (computed from the summary, nothing extra is stored) ----

    /// <summary>Badges are only visible from this score upwards (Trusted).</summary>
    public const int BadgesMinScore = 70;

    public static List<Models.BadgeDto> BadgesFor(Models.ReputationSummary s)
    {
        Models.BadgeDto B(string key, string title, string desc, bool earned) =>
            new() { Key = key, Title = title, Description = desc, Earned = earned };

        return new List<Models.BadgeDto>
        {
            B("first_report", "First accepted report", "Staff accepted your first report.", s.Confirmed >= 1),
            B("five_reports", "Reliable reporter", "5 of your reports were accepted.", s.Confirmed >= 5),
            B("ten_reports", "City helper", "10 of your reports were accepted.", s.Confirmed >= 10),
            B("clean_record", "Clean record", "5 accepted reports and no 'not found' rejections.", s.Confirmed >= 5 && s.FalseReports == 0),
            B("trusted", "Trusted", "Reached a trust score of 70.", s.Score >= 70),
            B("guardian", "Guardian", "Reached a trust score of 90.", s.Score >= 90),
        };
    }
}
