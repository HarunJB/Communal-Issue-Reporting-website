using Dapper;
using Npgsql;
using product_website.Models;
using product_website.Services;

namespace product_website.Data;

/// <summary>Numbers and lists for the dashboard home page. An operator's numbers cover their department only.</summary>
public class DashboardRepository
{
    private readonly NpgsqlDataSource _db;

    public DashboardRepository(NpgsqlDataSource db) => _db = db;

    private sealed class StatusCountRow
    {
        public string Status { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public async Task<DashboardViewModel> GetAsync(int? scopeDepartmentId, bool isAdmin)
    {
        await using var conn = await _db.OpenConnectionAsync();
        var p = new { scope = scopeDepartmentId };

        // "@scope::int IS NULL" means "no department filter" (admins).
        const string scopeSql = "(@scope::int IS NULL OR r.department_id = @scope)";

        var model = new DashboardViewModel { IsAdmin = isAdmin };

        model.Kpis = await conn.QuerySingleAsync<DashboardKpis>($@"
            SELECT
              COUNT(*) FILTER (WHERE r.status IN ('submitted','in_review','assigned','in_progress'))::int AS open,
              COUNT(*) FILTER (WHERE r.status IN ('submitted','in_review','assigned','in_progress')
                                 AND r.assigned_to IS NULL)::int AS unassigned,
              COUNT(*) FILTER (WHERE r.created_at >= date_trunc('day', now() AT TIME ZONE 'Europe/Sarajevo')
                                                     AT TIME ZONE 'Europe/Sarajevo')::int AS new_today,
              COUNT(*) FILTER (WHERE r.status = 'resolved' AND r.resolved_at >= now() - interval '7 days')::int AS resolved_last7_days,
              (AVG(EXTRACT(EPOCH FROM (r.resolved_at - r.created_at)) / 3600.0)
                 FILTER (WHERE r.status = 'resolved' AND r.resolved_at >= now() - interval '30 days'))::float8 AS avg_resolution_hours,
              COUNT(*) FILTER (WHERE r.status IN ('submitted','in_review','assigned','in_progress')
                                 AND r.created_at < now() - interval '7 days')::int AS older_than7_days
            FROM reports r
            WHERE r.archived_at IS NULL AND {scopeSql}", p);

        var statusRows = await conn.QueryAsync<StatusCountRow>($@"
            SELECT r.status::text AS status, COUNT(*)::int AS count
            FROM reports r
            WHERE r.archived_at IS NULL AND {scopeSql}
            GROUP BY r.status", p);
        model.StatusCounts = UiFormat.AllStatuses.ToDictionary(s => s, _ => 0);
        foreach (var row in statusRows) model.StatusCounts[row.Status] = row.Count;

        model.OpenByCategory = (await conn.QueryAsync<CountRow>($@"
            SELECT c.name_en AS label, COUNT(*)::int AS count
            FROM reports r JOIN categories c ON c.id = r.category_id
            WHERE r.archived_at IS NULL
              AND r.status IN ('submitted','in_review','assigned','in_progress')
              AND {scopeSql}
            GROUP BY c.name_en
            ORDER BY count DESC, c.name_en
            LIMIT 8", p)).ToList();

        // Reports created and resolved per day for the last 14 days (Sarajevo days).
        model.Days = (await conn.QueryAsync<DayRow>($@"
            SELECT g.day::timestamp AS day,
                   (SELECT COUNT(*)::int FROM reports r
                     WHERE (r.created_at AT TIME ZONE 'Europe/Sarajevo')::date = g.day AND {scopeSql}) AS created,
                   (SELECT COUNT(*)::int FROM reports r
                     WHERE r.resolved_at IS NOT NULL
                       AND (r.resolved_at AT TIME ZONE 'Europe/Sarajevo')::date = g.day AND {scopeSql}) AS resolved
            FROM generate_series(
                   ((now() AT TIME ZONE 'Europe/Sarajevo')::date - 13),
                   (now() AT TIME ZONE 'Europe/Sarajevo')::date,
                   interval '1 day') AS g(day)
            ORDER BY g.day", p)).ToList();

        // Admins: high priority first, then oldest. Operators: simply the oldest.
        var order = isAdmin
            ? "CASE r.priority WHEN 'high' THEN 0 WHEN 'normal' THEN 1 ELSE 2 END, r.created_at ASC"
            : "r.created_at ASC";

        model.NeedsAttention = (await conn.QueryAsync<ReportListItem>($@"
            SELECT r.id, c.name_en AS category_name, c.icon AS category_icon,
                   r.status::text AS status, r.priority::text AS priority,
                   r.address, r.description, r.created_at,
                   (r.archived_at IS NOT NULL) AS is_archived,
                   d.name_en AS department_name,
                   NULLIF(TRIM(CONCAT(au.first_name, ' ', au.last_name)), '') AS assigned_name,
                   NULL::text AS thumbnail_url,
                   (SELECT COUNT(*)::int FROM report_supports s WHERE s.report_id = r.id) AS support_count
            FROM reports r
            JOIN categories c ON c.id = r.category_id
            JOIN departments d ON d.id = r.department_id
            LEFT JOIN users au ON au.id = r.assigned_to
            WHERE r.archived_at IS NULL
              AND r.status IN ('submitted','in_review','assigned','in_progress')
              AND {scopeSql}
            ORDER BY {order}
            LIMIT 8", p)).ToList();

        if (isAdmin)
        {
            model.Departments = (await conn.QueryAsync<DepartmentStatRow>(@"
                SELECT d.id, d.name_en AS name,
                       COUNT(r.id) FILTER (WHERE r.status IN ('submitted','in_review','assigned','in_progress'))::int AS open,
                       COUNT(r.id) FILTER (WHERE r.status = 'resolved')::int AS resolved,
                       (AVG(EXTRACT(EPOCH FROM (r.resolved_at - r.created_at)) / 3600.0)
                          FILTER (WHERE r.status = 'resolved'))::float8 AS avg_resolution_hours
                FROM departments d
                LEFT JOIN reports r ON r.department_id = d.id AND r.archived_at IS NULL
                WHERE d.is_active
                GROUP BY d.id, d.name_en
                ORDER BY open DESC, d.name_en")).ToList();
        }

        return model;
    }
}
