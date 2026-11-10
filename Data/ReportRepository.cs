using System.Text.Json;
using Dapper;
using Npgsql;
using product_website.Models;

namespace product_website.Data;

public enum StatusChangeResult { Ok, NotFound, Forbidden, Unchanged }

public class ReportRepository
{
    private readonly NpgsqlDataSource _db;

    public ReportRepository(NpgsqlDataSource db) => _db = db;

    // ---------- Shared SQL pieces ----------

    private const string SummaryColumns = @"
        r.id, r.category_id,
        c.name_en AS category_name_en, c.name_bs AS category_name_bs, c.icon AS category_icon,
        r.status::text AS status, r.address,
        r.latitude::float8 AS latitude, r.longitude::float8 AS longitude,
        r.description, r.created_at, r.is_anonymous,
        (r.user_id = @userId) AS is_mine,
        (SELECT p.url FROM report_photos p
          WHERE p.report_id = r.id AND p.kind = 'report' ORDER BY p.id LIMIT 1) AS thumbnail_url,
        (SELECT COUNT(*)::int FROM report_supports s WHERE s.report_id = r.id) AS support_count,
        EXISTS (SELECT 1 FROM report_supports s
                 WHERE s.report_id = r.id AND s.user_id = @userId) AS supported_by_me";

    private const string SummaryFrom = @"
        FROM reports r
        JOIN categories c ON c.id = r.category_id";

    // Distance in metres between the report and (@lat, @lng): haversine formula.
    private const string DistanceMeters = @"
        (6371000 * 2 * ASIN(SQRT(
            POWER(SIN(RADIANS((r.latitude::float8 - @lat) / 2)), 2) +
            COS(RADIANS(@lat)) * COS(RADIANS(r.latitude::float8)) *
            POWER(SIN(RADIANS((r.longitude::float8 - @lng) / 2)), 2))))";

    private static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["submitted"] = "Submitted",
        ["in_review"] = "In review",
        ["assigned"] = "Assigned",
        ["in_progress"] = "In progress",
        ["resolved"] = "Resolved",
        ["rejected"] = "Rejected"
    };

    // ---------- Lists ----------

    /// <summary>Public feed: every report that is not archived or rejected, newest first.</summary>
    public async Task<List<ReportSummary>> GetFeedAsync(int userId, int? categoryId, int limit, int offset)
    {
        var sql = $@"
            SELECT {SummaryColumns} {SummaryFrom}
            WHERE r.archived_at IS NULL AND r.status <> 'rejected'"
            + (categoryId.HasValue ? " AND r.category_id = @categoryId" : "")
            + " ORDER BY r.created_at DESC, r.id DESC LIMIT @limit OFFSET @offset";

        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<ReportSummary>(sql, new { userId, categoryId, limit, offset });
        return rows.ToList();
    }

    /// <summary>The user's own reports, including rejected and archived ones.</summary>
    public async Task<List<ReportSummary>> GetMineAsync(int userId)
    {
        var sql = $@"
            SELECT {SummaryColumns} {SummaryFrom}
            WHERE r.user_id = @userId
            ORDER BY r.created_at DESC, r.id DESC";

        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<ReportSummary>(sql, new { userId });
        return rows.ToList();
    }

    /// <summary>Open reports of the same category within the given distance, closest first.</summary>
    public async Task<List<ReportSummary>> GetNearbyAsync(
        int userId, double lat, double lng, int categoryId, double radius, int limit)
    {
        var sql = $@"
            SELECT {SummaryColumns} {SummaryFrom}
            WHERE r.category_id = @categoryId
              AND r.archived_at IS NULL
              AND r.status NOT IN ('resolved', 'rejected')
              AND {DistanceMeters} <= @radius
            ORDER BY {DistanceMeters} ASC
            LIMIT @limit";

        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<ReportSummary>(sql, new { userId, lat, lng, categoryId, radius, limit });
        return rows.ToList();
    }

    // ---------- Detail ----------

    public async Task<ReportDetail?> GetDetailAsync(int id, int userId)
    {
        var sql = $@"
            SELECT {SummaryColumns},
                   r.user_id, r.archived_at,
                   d.name_en AS department_name_en, d.name_bs AS department_name_bs,
                   CASE WHEN r.is_anonymous THEN NULL
                        ELSE COALESCE(u.display_name, u.first_name) END AS author_name
            {SummaryFrom}
            JOIN departments d ON d.id = r.department_id
            JOIN users u ON u.id = r.user_id
            WHERE r.id = @id";

        await using var conn = await _db.OpenConnectionAsync();
        var detail = await conn.QuerySingleOrDefaultAsync<ReportDetail>(sql, new { id, userId });
        if (detail is null) return null;

        detail.Photos = (await conn.QueryAsync<PhotoDto>(
            "SELECT url, kind::text AS kind FROM report_photos WHERE report_id = @id ORDER BY id",
            new { id })).ToList();

        detail.History = (await conn.QueryAsync<HistoryDto>(@"
            SELECT old_status::text AS old_status, new_status::text AS new_status, note, created_at
            FROM status_history
            WHERE report_id = @id
            ORDER BY created_at DESC, id DESC",
            new { id })).ToList();

        // Only public responses. Internal staff notes (is_internal) are never sent to citizens.
        detail.Comments = (await conn.QueryAsync<CommentDto>(@"
            SELECT COALESCE(u.display_name, u.first_name) AS author_name,
                   u.role::text AS author_role,
                   c.body, c.created_at
            FROM comments c
            JOIN users u ON u.id = c.user_id
            WHERE c.report_id = @id AND NOT c.is_internal
            ORDER BY c.created_at DESC, c.id DESC",
            new { id })).ToList();

        return detail;
    }

    public async Task<ReportBasic?> GetBasicAsync(int id)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<ReportBasic>(
            "SELECT user_id, status::text AS status, archived_at FROM reports WHERE id = @id",
            new { id });
    }

    // ---------- Create ----------

    /// <summary>Inserts the report, its photos and its first status-history row in one transaction.</summary>
    public async Task<int> CreateAsync(
        int userId, CategoryDto category, double latitude, double longitude,
        string? address, string? description, bool isAnonymous, IReadOnlyList<string> photoUrls)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // The department and the starting priority both come from the category.
        var reportId = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO reports
                (user_id, category_id, department_id, description, latitude, longitude,
                 address, priority, is_anonymous, source)
            VALUES
                (@userId, @categoryId, @departmentId, @description,
                 CAST(@latitude AS numeric), CAST(@longitude AS numeric),
                 @address, CAST(@priority AS priority_level), @isAnonymous, 'mobile')
            RETURNING id",
            new
            {
                userId,
                categoryId = category.Id,
                departmentId = category.DepartmentId,
                description,
                latitude,
                longitude,
                address,
                priority = category.DefaultPriority,
                isAnonymous
            }, tx);

        foreach (var url in photoUrls)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO report_photos (report_id, url, kind, uploaded_by)
                VALUES (@reportId, @url, 'report', @userId)",
                new { reportId, url, userId }, tx);
        }

        await conn.ExecuteAsync(@"
            INSERT INTO status_history (report_id, old_status, new_status, changed_by)
            VALUES (@reportId, NULL, 'submitted', @userId)",
            new { reportId, userId }, tx);

        await tx.CommitAsync();
        return reportId;
    }

    // ---------- Supports ----------

    public async Task AddSupportAsync(int reportId, int userId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO report_supports (report_id, user_id)
            VALUES (@reportId, @userId)
            ON CONFLICT DO NOTHING",
            new { reportId, userId });
    }

    public async Task RemoveSupportAsync(int reportId, int userId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "DELETE FROM report_supports WHERE report_id = @reportId AND user_id = @userId",
            new { reportId, userId });
    }

    public async Task<SupportResult> GetSupportStateAsync(int reportId, int userId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.QuerySingleAsync<SupportResult>(@"
            SELECT (SELECT COUNT(*)::int FROM report_supports WHERE report_id = @reportId) AS support_count,
                   EXISTS (SELECT 1 FROM report_supports
                            WHERE report_id = @reportId AND user_id = @userId) AS supported_by_me",
            new { reportId, userId });
    }

    // ---------- Staff: change status ----------

    public class StatusTarget
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int DepartmentId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string CategoryName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Changes a report's status and records it everywhere it belongs: the report row, the status history,
    /// an optional public response, a notice for the author and every supporter, and the audit log.
    /// <paramref name="staffDepartmentId"/> is the operator's department (null for admins, who may change any report).
    /// </summary>
    public async Task<StatusChangeResult> ChangeStatusAsync(
        int reportId, string newStatus, int staffId, int? staffDepartmentId, string? note, string? publicComment)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var target = await conn.QuerySingleOrDefaultAsync<StatusTarget>(@"
            SELECT r.id, r.user_id, r.department_id, r.status::text AS status, r.address,
                   c.name_en AS category_name
            FROM reports r
            JOIN categories c ON c.id = r.category_id
            WHERE r.id = @reportId
            FOR UPDATE OF r",
            new { reportId }, tx);

        if (target is null) return StatusChangeResult.NotFound;
        if (staffDepartmentId.HasValue && staffDepartmentId.Value != target.DepartmentId)
            return StatusChangeResult.Forbidden;
        if (target.Status == newStatus) return StatusChangeResult.Unchanged;

        await conn.ExecuteAsync(@"
            UPDATE reports
            SET status = CAST(@newStatus AS report_status),
                updated_at = now(),
                resolved_at = CASE WHEN @newStatus = 'resolved' THEN now() ELSE NULL END,
                rejection_reason = CASE WHEN @newStatus = 'rejected' THEN @note ELSE NULL END,
                assigned_to = CASE WHEN @newStatus IN ('assigned', 'in_progress') AND assigned_to IS NULL
                                   THEN @staffId ELSE assigned_to END
            WHERE id = @reportId",
            new { reportId, newStatus, note, staffId }, tx);

        await conn.ExecuteAsync(@"
            INSERT INTO status_history (report_id, old_status, new_status, changed_by, note)
            VALUES (@reportId, CAST(@oldStatus AS report_status), CAST(@newStatus AS report_status), @staffId, @note)",
            new { reportId, oldStatus = target.Status, newStatus, staffId, note }, tx);

        if (!string.IsNullOrWhiteSpace(publicComment))
        {
            await conn.ExecuteAsync(@"
                INSERT INTO comments (report_id, user_id, body, is_internal)
                VALUES (@reportId, @staffId, @body, FALSE)",
                new { reportId, staffId, body = publicComment.Trim() }, tx);
        }

        // A notice for the author and for everyone who supported the report (except the staff member).
        var label = StatusLabels.GetValueOrDefault(newStatus, newStatus);
        var place = string.IsNullOrWhiteSpace(target.Address) ? "" : $" at {target.Address}";
        var title = $"Report #{reportId}: {label}";
        var body = $"{target.CategoryName}{place}: status changed to {label.ToLowerInvariant()}."
                   + (string.IsNullOrWhiteSpace(note) ? "" : $" {note.Trim()}");

        await conn.ExecuteAsync(@"
            INSERT INTO notifications (user_id, report_id, title, body)
            SELECT t.uid, @reportId, @title, @body
            FROM (
                SELECT user_id AS uid FROM reports WHERE id = @reportId
                UNION
                SELECT user_id FROM report_supports WHERE report_id = @reportId
            ) t
            WHERE t.uid <> @staffId",
            new { reportId, title, body, staffId }, tx);

        await conn.ExecuteAsync(@"
            INSERT INTO audit_log (actor_id, action, entity_type, entity_id, details)
            VALUES (@staffId, 'report.status_changed', 'report', @reportId, CAST(@details AS jsonb))",
            new
            {
                staffId,
                reportId,
                details = JsonSerializer.Serialize(new { from = target.Status, to = newStatus, note })
            }, tx);

        await tx.CommitAsync();
        return StatusChangeResult.Ok;
    }
}
