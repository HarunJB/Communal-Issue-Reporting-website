using System.Text.Json;
using Dapper;
using Npgsql;
using product_website.Models;
using product_website.Services;

namespace product_website.Data;

/// <summary>
/// Reads and changes the trust scores. The score changes in exactly two ways:
/// a staff decision on a report (ApplyVerdictAsync, called from ReportRepository.ChangeStatusAsync)
/// and a manual adjustment by an admin (AdjustAsync). Both write a row to reputation_events.
/// </summary>
public class ReputationRepository
{
    private readonly NpgsqlDataSource _db;

    public ReputationRepository(NpgsqlDataSource db) => _db = db;

    // ---------- Reading ----------

    private sealed class SummaryRow
    {
        public int Score { get; set; }
        public int Confirmed { get; set; }
        public int FalseReports { get; set; }
        public int ReportsToday { get; set; }
    }

    public static ReputationSummary BuildSummary(int score, int confirmed, int falseReports, int reportsToday)
    {
        var level = ReputationRules.LevelFor(score);
        var next = ReputationRules.NextLevel(score);
        return new ReputationSummary
        {
            Score = score,
            Level = level.Key,
            LevelLabel = level.Label,
            LevelMin = level.Min,
            NextLevelLabel = next?.Label,
            NextLevelMin = next?.Min,
            DailyLimit = level.DailyReports,
            ReportsToday = reportsToday,
            Confirmed = confirmed,
            FalseReports = falseReports
        };
    }

    public async Task<ReputationSummary?> GetSummaryAsync(int userId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        var row = await conn.QuerySingleOrDefaultAsync<SummaryRow>(@"
            SELECT u.trust_score AS score,
                   (SELECT COUNT(*)::int FROM reputation_events e
                     WHERE e.user_id = u.id AND e.kind = 'confirmed' AND e.reversed_at IS NULL) AS confirmed,
                   (SELECT COUNT(*)::int FROM reputation_events e
                     WHERE e.user_id = u.id AND e.kind = 'false_report' AND e.reversed_at IS NULL) AS false_reports,
                   (SELECT COUNT(*)::int FROM reports r
                     WHERE r.user_id = u.id AND r.created_at > now() - interval '24 hours') AS reports_today
            FROM users u WHERE u.id = @userId", new { userId });

        return row is null ? null : BuildSummary(row.Score, row.Confirmed, row.FalseReports, row.ReportsToday);
    }

    public async Task<List<ReputationEventRow>> GetEventsAsync(int userId, int limit)
    {
        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<ReputationEventRow>(@"
            SELECT id, report_id, kind, delta, score_after, note, created_at, reversed_at
            FROM reputation_events
            WHERE user_id = @userId
            ORDER BY created_at DESC, id DESC
            LIMIT @limit", new { userId, limit });
        return rows.ToList();
    }

    /// <summary>
    /// Used before a new report is saved: is the user still under the daily limit of their level?
    /// Staff are never limited. Returns the message to show when they are over it, or null.
    /// </summary>
    public async Task<string?> CheckDailyLimitAsync(int userId, bool isStaff)
    {
        if (isStaff) return null;

        var summary = await GetSummaryAsync(userId);
        if (summary is null || summary.ReportsToday < summary.DailyLimit) return null;

        return $"You have reached today's limit of {summary.DailyLimit} reports for the \"{summary.LevelLabel}\" trust level. "
               + "Please try again tomorrow. Reports that staff confirm raise your trust score and your limit.";
    }

    // ---------- A staff decision on a report ----------

    private sealed class AuthorRow
    {
        public string Role { get; set; } = string.Empty;
        public int TrustScore { get; set; }
    }

    private sealed class ActiveRow
    {
        public int Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public int Delta { get; set; }
    }

    /// <summary>
    /// Called inside the status-change transaction. Works out what the report's NEW status means for the
    /// reporter ("confirmed", "false report" or nothing), undoes the effect of an earlier decision on the same
    /// report if it was different, and applies the new one. Returns null when the score does not change.
    /// Only citizen accounts have a score.
    /// </summary>
    public static async Task<VerdictOutcome?> ApplyVerdictAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        int reportId, int authorId, string newStatus, string? rejectionCode, int actorId)
    {
        // Locking the user row means two decisions on one person's reports can never mix up the numbers.
        var author = await conn.QuerySingleOrDefaultAsync<AuthorRow>(@"
            SELECT role::text AS role, trust_score
            FROM users WHERE id = @authorId AND deleted_at IS NULL
            FOR UPDATE", new { authorId }, tx);
        if (author is null || author.Role != "citizen") return null;

        string? kind = newStatus is "assigned" or "in_progress" or "resolved" ? "confirmed"
            : newStatus == "rejected" && rejectionCode == "not_found" ? "false_report"
            : null;

        var active = await conn.QuerySingleOrDefaultAsync<ActiveRow>(@"
            SELECT id, kind, delta FROM reputation_events
            WHERE report_id = @reportId AND kind IN ('confirmed', 'false_report') AND reversed_at IS NULL
            ORDER BY id DESC LIMIT 1
            FOR UPDATE", new { reportId }, tx);

        if (active?.Kind == kind) return null; // same decision as before (or still none): nothing to do

        var start = author.TrustScore;
        var score = start;

        if (active is not null)
        {
            var undone = ReputationRules.Clamp(score - active.Delta);
            await conn.ExecuteAsync(
                "UPDATE reputation_events SET reversed_at = now() WHERE id = @id", new { id = active.Id }, tx);
            await conn.ExecuteAsync(@"
                INSERT INTO reputation_events (user_id, report_id, kind, delta, score_after, note, created_by)
                VALUES (@authorId, @reportId, 'reversal', @delta, @after, @note, @actorId)",
                new
                {
                    authorId, reportId, delta = undone - score, after = undone, actorId,
                    note = "A later decision on this report replaced the earlier one."
                }, tx);
            score = undone;
        }

        if (kind is not null)
        {
            var wanted = kind == "confirmed" ? ReputationRules.ConfirmedPoints : -ReputationRules.FalsePenalty;
            var after = ReputationRules.Clamp(score + wanted);
            await conn.ExecuteAsync(@"
                INSERT INTO reputation_events (user_id, report_id, kind, delta, score_after, created_by)
                VALUES (@authorId, @reportId, @kind, @delta, @after, @actorId)",
                new { authorId, reportId, kind, delta = after - score, after, actorId }, tx);
            score = after;
        }

        await conn.ExecuteAsync(
            "UPDATE users SET trust_score = @score WHERE id = @authorId", new { score, authorId }, tx);

        return new VerdictOutcome
        {
            NetDelta = score - start,
            ScoreAfter = score,
            LevelLabel = ReputationRules.LevelLabel(score),
            LevelChanged = ReputationRules.LevelFor(start).Key != ReputationRules.LevelFor(score).Key
        };
    }

    // ---------- A manual change by an admin ----------

    /// <summary>Adds or removes points by hand. Returns the new score, or null if the user is not a citizen.</summary>
    public async Task<int?> AdjustAsync(int adminId, int userId, int delta, string note)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var user = await conn.QuerySingleOrDefaultAsync<AuthorRow>(@"
            SELECT role::text AS role, trust_score
            FROM users WHERE id = @userId AND deleted_at IS NULL
            FOR UPDATE", new { userId }, tx);
        if (user is null || user.Role != "citizen") return null;

        var after = ReputationRules.Clamp(user.TrustScore + delta);
        var applied = after - user.TrustScore;

        await conn.ExecuteAsync(@"
            INSERT INTO reputation_events (user_id, kind, delta, score_after, note, created_by)
            VALUES (@userId, 'admin_adjust', @applied, @after, @note, @adminId)",
            new { userId, applied, after, note, adminId }, tx);

        await conn.ExecuteAsync(
            "UPDATE users SET trust_score = @after WHERE id = @userId", new { after, userId }, tx);

        await conn.ExecuteAsync(@"
            INSERT INTO notifications (user_id, title, body)
            VALUES (@userId, 'Trust score updated', @body)",
            new
            {
                userId,
                body = $"A staff member changed your trust score by {applied:+0;-0} (now {after}). Reason: {note}"
            }, tx);

        await conn.ExecuteAsync(@"
            INSERT INTO audit_log (actor_id, action, entity_type, entity_id, details)
            VALUES (@adminId, 'user.score_adjusted', 'user', @userId, CAST(@details AS jsonb))",
            new
            {
                adminId,
                userId,
                details = JsonSerializer.Serialize(new { from = user.TrustScore, to = after, requested = delta, note })
            }, tx);

        await tx.CommitAsync();
        return after;
    }
}
