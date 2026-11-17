using System.Text.Json;
using Dapper;
using Npgsql;
using product_website.Models;
using product_website.Services;

namespace product_website.Data;

/// <summary>Everything the staff dashboard does with reports. Operators are limited to their own department.</summary>
public class StaffReportRepository
{
    private readonly NpgsqlDataSource _db;

    public StaffReportRepository(NpgsqlDataSource db) => _db = db;

    public const int PageSize = 20;

    private sealed class AssigneeRow
    {
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int? DepartmentId { get; set; }
    }

    private sealed class ReassignRow
    {
        public int CategoryId { get; set; }
        public int DepartmentId { get; set; }
    }

    private sealed class StatusCountRow
    {
        public string Status { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    // ---------- Lookups for filters ----------

    public async Task<List<Option>> GetCategoryOptionsAsync(int? departmentId = null)
    {
        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<Option>(@"
            SELECT id, name_en AS name FROM categories
            WHERE (@departmentId::int IS NULL OR department_id = @departmentId)
            ORDER BY name_en", new { departmentId });
        return rows.ToList();
    }

    public async Task<List<Option>> GetDepartmentOptionsAsync()
    {
        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<Option>("SELECT id, name_en AS name FROM departments ORDER BY name_en");
        return rows.ToList();
    }

    public async Task<string?> GetDepartmentNameAsync(int id)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string?>("SELECT name_en FROM departments WHERE id = @id", new { id });
    }

    // ---------- List ----------

    /// <summary>Builds the WHERE part shared by the list, the counts and the map.</summary>
    private static string BuildWhere(
        ReportFilter f, int? scopeDepartmentId, int staffId, bool isAdmin, bool includeStatus, DynamicParameters p)
    {
        var where = new List<string> { f.Archived ? "r.archived_at IS NOT NULL" : "r.archived_at IS NULL" };

        // Operators only ever see their own department.
        if (scopeDepartmentId.HasValue)
        {
            where.Add("r.department_id = @scopeDepartmentId");
            p.Add("scopeDepartmentId", scopeDepartmentId.Value);
        }
        else if (f.DepartmentId.HasValue)
        {
            where.Add("r.department_id = @departmentId");
            p.Add("departmentId", f.DepartmentId.Value);
        }

        if (f.CategoryId.HasValue)
        {
            where.Add("r.category_id = @categoryId");
            p.Add("categoryId", f.CategoryId.Value);
        }

        if (isAdmin && f.Priority is "low" or "normal" or "high")
        {
            where.Add("r.priority = CAST(@priority AS priority_level)");
            p.Add("priority", f.Priority);
        }

        if (f.Assigned == "me")
        {
            where.Add("r.assigned_to = @staffId");
            p.Add("staffId", staffId);
        }
        else if (f.Assigned == "none")
        {
            where.Add("r.assigned_to IS NULL");
        }

        if (f.From.HasValue)
        {
            where.Add("r.created_at >= @dateFrom");
            p.Add("dateFrom", DateTime.SpecifyKind(f.From.Value.Date, DateTimeKind.Utc));
        }
        if (f.To.HasValue)
        {
            where.Add("r.created_at < @dateTo");
            p.Add("dateTo", DateTime.SpecifyKind(f.To.Value.Date.AddDays(1), DateTimeKind.Utc));
        }

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var q = f.Q.Trim();
            var escaped = q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            where.Add(@"(r.address ILIKE @likeText OR r.description ILIKE @likeText OR c.name_en ILIKE @likeText
                         OR c.name_bs ILIKE @likeText OR r.id::text = @exact)");
            p.Add("likeText", $"%{escaped}%");
            p.Add("exact", q.TrimStart('#'));
        }

        if (includeStatus)
        {
            if (f.Status == "open")
                where.Add("r.status IN ('submitted', 'in_review', 'assigned', 'in_progress')");
            else if (f.Status != "all" && UiFormat.AllStatuses.Contains(f.Status))
            {
                where.Add("r.status = CAST(@status AS report_status)");
                p.Add("status", f.Status);
            }
        }

        return " WHERE " + string.Join(" AND ", where);
    }

    private const string FromClause = @"
        FROM reports r
        JOIN categories c ON c.id = r.category_id
        JOIN departments d ON d.id = r.department_id
        LEFT JOIN users au ON au.id = r.assigned_to";

    public async Task<ReportListViewModel> ListAsync(
        ReportFilter filter, int? scopeDepartmentId, int staffId, bool isAdmin)
    {
        filter.Page = Math.Max(1, filter.Page);
        if (string.IsNullOrWhiteSpace(filter.Status)) filter.Status = "open";

        await using var conn = await _db.OpenConnectionAsync();

        // Counts per status (for the status pills), honoring every filter except the status itself.
        var countParams = new DynamicParameters();
        var countWhere = BuildWhere(filter, scopeDepartmentId, staffId, isAdmin, includeStatus: false, countParams);
        var countRows = await conn.QueryAsync<StatusCountRow>(
            $"SELECT r.status::text AS status, COUNT(*)::int AS count {FromClause} {countWhere} GROUP BY r.status",
            countParams);

        var counts = UiFormat.AllStatuses.ToDictionary(s => s, _ => 0);
        foreach (var row in countRows) counts[row.Status] = row.Count;
        counts["open"] = UiFormat.OpenStatuses.Sum(s => counts[s]);
        counts["all"] = UiFormat.AllStatuses.Sum(s => counts[s]);

        var total = counts.TryGetValue(filter.Status, out var t) ? t : counts["all"];

        var orderBy = filter.Sort switch
        {
            "newest" => "r.created_at DESC",
            "supports" => "support_count DESC, r.created_at ASC",
            "priority" when isAdmin =>
                "CASE r.priority WHEN 'high' THEN 0 WHEN 'normal' THEN 1 ELSE 2 END, r.created_at ASC",
            _ => "r.created_at ASC"
        };

        var p = new DynamicParameters();
        var where = BuildWhere(filter, scopeDepartmentId, staffId, isAdmin, includeStatus: true, p);
        p.Add("limit", PageSize);
        p.Add("offset", (filter.Page - 1) * PageSize);

        var items = await conn.QueryAsync<ReportListItem>($@"
            SELECT r.id, c.name_en AS category_name, c.icon AS category_icon,
                   r.status::text AS status, r.priority::text AS priority,
                   r.address, r.description, r.created_at,
                   (r.archived_at IS NOT NULL) AS is_archived,
                   d.name_en AS department_name,
                   NULLIF(TRIM(CONCAT(au.first_name, ' ', au.last_name)), '') AS assigned_name,
                   (SELECT ph.url FROM report_photos ph
                     WHERE ph.report_id = r.id AND ph.kind = 'report' ORDER BY ph.id LIMIT 1) AS thumbnail_url,
                   (SELECT COUNT(*)::int FROM report_supports s WHERE s.report_id = r.id) AS support_count
            {FromClause} {where}
            ORDER BY {orderBy}
            LIMIT @limit OFFSET @offset", p);

        return new ReportListViewModel
        {
            Filter = filter,
            Items = items.ToList(),
            Counts = counts,
            Total = total,
            Page = filter.Page,
            PageSize = PageSize,
            IsAdmin = isAdmin
        };
    }

    public async Task<List<MapPoint>> GetMapPointsAsync(
        ReportFilter filter, int? scopeDepartmentId, int staffId, bool isAdmin)
    {
        if (string.IsNullOrWhiteSpace(filter.Status)) filter.Status = "open";

        var p = new DynamicParameters();
        var where = BuildWhere(filter, scopeDepartmentId, staffId, isAdmin, includeStatus: true, p);

        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<MapPoint>($@"
            SELECT r.id, r.latitude::float8 AS latitude, r.longitude::float8 AS longitude,
                   r.status::text AS status, r.priority::text AS priority,
                   c.name_en AS category, r.address
            {FromClause} {where}
            ORDER BY r.created_at DESC
            LIMIT 1500", p);
        return rows.ToList();
    }

    // ---------- Detail ----------

    public async Task<StaffReportDetail?> GetDetailAsync(int id)
    {
        await using var conn = await _db.OpenConnectionAsync();

        var report = await conn.QuerySingleOrDefaultAsync<StaffReportDetail>(@"
            SELECT r.id, r.category_id, c.name_en AS category_name,
                   r.department_id, d.name_en AS department_name,
                   r.status::text AS status, r.priority::text AS priority,
                   r.address, r.latitude::float8 AS latitude, r.longitude::float8 AS longitude,
                   r.description, r.is_anonymous, r.source::text AS source, r.rejection_reason,
                   r.created_at, r.updated_at, r.resolved_at, r.archived_at,
                   r.assigned_to,
                   NULLIF(TRIM(CONCAT(au.first_name, ' ', au.last_name)), '') AS assigned_name,
                   u.id AS author_id,
                   TRIM(CONCAT(u.first_name, ' ', u.last_name)) AS author_name,
                   u.email AS author_email, u.phone AS author_phone,
                   (SELECT COUNT(*)::int FROM report_supports s WHERE s.report_id = r.id) AS support_count,
                   CASE WHEN u.role = 'citizen' THEN u.trust_score END AS author_trust_score,
                   (SELECT COUNT(*)::int FROM reputation_events e
                     WHERE e.user_id = u.id AND e.kind = 'confirmed' AND e.reversed_at IS NULL) AS author_confirmed,
                   (SELECT COUNT(*)::int FROM reputation_events e
                     WHERE e.user_id = u.id AND e.kind = 'false_report' AND e.reversed_at IS NULL) AS author_false_reports
            FROM reports r
            JOIN categories c ON c.id = r.category_id
            JOIN departments d ON d.id = r.department_id
            JOIN users u ON u.id = r.user_id
            LEFT JOIN users au ON au.id = r.assigned_to
            WHERE r.id = @id", new { id });

        if (report is null) return null;

        report.Photos = (await conn.QueryAsync<StaffPhotoItem>(@"
            SELECT url, kind::text AS kind FROM report_photos
            WHERE report_id = @id ORDER BY kind, id", new { id })).ToList();

        report.History = (await conn.QueryAsync<StaffHistoryItem>(@"
            SELECT h.old_status::text AS old_status, h.new_status::text AS new_status, h.note,
                   TRIM(CONCAT(u.first_name, ' ', u.last_name)) AS changed_by_name, h.created_at
            FROM status_history h
            JOIN users u ON u.id = h.changed_by
            WHERE h.report_id = @id
            ORDER BY h.created_at DESC, h.id DESC", new { id })).ToList();

        report.Comments = (await conn.QueryAsync<StaffCommentItem>(@"
            SELECT cm.id, TRIM(CONCAT(u.first_name, ' ', u.last_name)) AS author_name,
                   u.role::text AS author_role, cm.body, cm.is_internal, cm.created_at
            FROM comments cm
            JOIN users u ON u.id = cm.user_id
            WHERE cm.report_id = @id
            ORDER BY cm.created_at DESC, cm.id DESC", new { id })).ToList();

        return report;
    }

    /// <summary>Staff who can be given a report: operators of that department, and all admins.</summary>
    public async Task<List<StaffOption>> GetAssignableAsync(int departmentId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<StaffOption>(@"
            SELECT id, TRIM(CONCAT(first_name, ' ', last_name)) AS name, role::text AS role
            FROM users
            WHERE status = 'active' AND deleted_at IS NULL
              AND ((role = 'operator' AND department_id = @departmentId) OR role = 'admin')
            ORDER BY role, first_name, last_name", new { departmentId });
        return rows.ToList();
    }

    public async Task<int?> GetReportDepartmentAsync(int reportId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int?>("SELECT department_id FROM reports WHERE id = @reportId", new { reportId });
    }

    // ---------- Actions ----------

    private static Task AuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, int actorId, string action, int reportId, object details) =>
        conn.ExecuteAsync(@"
            INSERT INTO audit_log (actor_id, action, entity_type, entity_id, details)
            VALUES (@actorId, @action, 'report', @reportId, CAST(@details AS jsonb))",
            new { actorId, action, reportId, details = JsonSerializer.Serialize(details) }, tx);

    /// <summary>Gives the report to a staff member. Returns that person's name, or null if they can't take it.</summary>
    public async Task<string?> AssignAsync(int reportId, int actorId, int assigneeId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var assignee = await conn.QuerySingleOrDefaultAsync<AssigneeRow>(@"
            SELECT TRIM(CONCAT(first_name, ' ', last_name)) AS name, role::text AS role, department_id
            FROM users WHERE id = @assigneeId AND status = 'active' AND deleted_at IS NULL",
            new { assigneeId }, tx);
        var departmentId = await conn.ExecuteScalarAsync<int?>(
            "SELECT department_id FROM reports WHERE id = @reportId FOR UPDATE", new { reportId }, tx);

        if (assignee is null || departmentId is null) return null;
        var ok = assignee.Role == "admin"
                 || (assignee.Role == "operator" && assignee.DepartmentId == departmentId);
        if (!ok) return null;

        await conn.ExecuteAsync(
            "UPDATE reports SET assigned_to = @assigneeId, updated_at = now() WHERE id = @reportId",
            new { reportId, assigneeId }, tx);
        await AuditAsync(conn, tx, actorId, "report.assigned", reportId,
            new { assignee = assigneeId, name = assignee.Name });

        await tx.CommitAsync();
        return assignee.Name;
    }

    public async Task UnassignAsync(int reportId, int actorId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync(
            "UPDATE reports SET assigned_to = NULL, updated_at = now() WHERE id = @reportId", new { reportId }, tx);
        await AuditAsync(conn, tx, actorId, "report.unassigned", reportId, new { });
        await tx.CommitAsync();
    }

    public async Task SetPriorityAsync(int reportId, int actorId, string priority)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var old = await conn.ExecuteScalarAsync<string?>(
            "SELECT priority::text FROM reports WHERE id = @reportId FOR UPDATE", new { reportId }, tx);
        if (old is null || old == priority) return;

        await conn.ExecuteAsync(@"
            UPDATE reports SET priority = CAST(@priority AS priority_level), updated_at = now()
            WHERE id = @reportId", new { reportId, priority }, tx);
        await AuditAsync(conn, tx, actorId, "report.priority_changed", reportId, new { from = old, to = priority });
        await tx.CommitAsync();
    }

    /// <summary>Admin: moves a report to another category and/or department.</summary>
    public async Task<bool> ReassignAsync(int reportId, int actorId, int categoryId, int departmentId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var valid = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (SELECT 1 FROM categories WHERE id = @categoryId)
               AND EXISTS (SELECT 1 FROM departments WHERE id = @departmentId AND is_active)",
            new { categoryId, departmentId }, tx);
        if (!valid) return false;

        var old = await conn.QuerySingleOrDefaultAsync<ReassignRow>(
            "SELECT category_id, department_id FROM reports WHERE id = @reportId FOR UPDATE", new { reportId }, tx);
        if (old is null) return false;

        // The handler may belong to the old department: clear them if the department changes.
        await conn.ExecuteAsync(@"
            UPDATE reports
            SET category_id = @categoryId,
                department_id = @departmentId,
                assigned_to = CASE WHEN @departmentId <> department_id THEN NULL ELSE assigned_to END,
                updated_at = now()
            WHERE id = @reportId", new { reportId, categoryId, departmentId }, tx);

        await AuditAsync(conn, tx, actorId, "report.reassigned", reportId, new
        {
            categoryFrom = old.CategoryId, categoryTo = categoryId,
            departmentFrom = old.DepartmentId, departmentTo = departmentId
        });
        await tx.CommitAsync();
        return true;
    }

    /// <summary>Adds a comment. A public one also sends the report's author a notice.</summary>
    public async Task AddCommentAsync(int reportId, int userId, string body, bool isInternal)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync(@"
            INSERT INTO comments (report_id, user_id, body, is_internal)
            VALUES (@reportId, @userId, @body, @isInternal)",
            new { reportId, userId, body = body.Trim(), isInternal }, tx);

        if (!isInternal)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO notifications (user_id, report_id, title, body)
                SELECT r.user_id, r.id, @title, @body
                FROM reports r WHERE r.id = @reportId AND r.user_id <> @userId",
                new { reportId, userId, title = $"Report #{reportId}: new reply from the city", body = UiFormat.Short(body, 200) }, tx);
        }

        await tx.CommitAsync();
    }

    /// <summary>Archives a finished report (instead of deleting it) or brings it back.</summary>
    public async Task<bool> SetArchivedAsync(int reportId, int actorId, bool archived)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var status = await conn.ExecuteScalarAsync<string?>(
            "SELECT status::text FROM reports WHERE id = @reportId FOR UPDATE", new { reportId }, tx);
        if (status is null) return false;
        if (archived && status is not ("resolved" or "rejected")) return false;

        await conn.ExecuteAsync(
            "UPDATE reports SET archived_at = CASE WHEN @archived THEN now() ELSE NULL END, updated_at = now() WHERE id = @reportId",
            new { reportId, archived }, tx);
        await AuditAsync(conn, tx, actorId, archived ? "report.archived" : "report.unarchived", reportId, new { });
        await tx.CommitAsync();
        return true;
    }

    public async Task<int> CountResolutionPhotosAsync(int reportId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM report_photos WHERE report_id = @reportId AND kind = 'resolution'", new { reportId });
    }

    public async Task AddResolutionPhotoAsync(int reportId, int userId, string url)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO report_photos (report_id, url, kind, uploaded_by)
            VALUES (@reportId, @url, 'resolution', @userId)", new { reportId, url, userId }, tx);
        await AuditAsync(conn, tx, userId, "report.resolution_photo_added", reportId, new { url });
        await tx.CommitAsync();
    }
}
