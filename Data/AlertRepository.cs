using Dapper;
using Npgsql;
using product_website.Models;

namespace product_website.Data;

/// <summary>Live counts for the header badges. Nothing is stored: the numbers are worked out from the reports.</summary>
public class AlertRepository
{
    private readonly NpgsqlDataSource _db;

    public AlertRepository(NpgsqlDataSource db) => _db = db;

    private const string Open = "r.status IN ('submitted','in_review','assigned','in_progress')";

    private const string NoActiveOperator = @"NOT EXISTS (
        SELECT 1 FROM users u
        WHERE u.department_id = r.department_id AND u.role = 'operator'
          AND u.status = 'active' AND u.deleted_at IS NULL)";

    private const string Triage =
        "(d.name_en = 'General Municipal Services' OR " + NoActiveOperator + ")";

    private const string Stale =
        "((r.assigned_to IS NULL AND r.created_at < now() - interval '24 hours') OR r.updated_at < now() - interval '7 days')";

    // "@scope::int IS NULL" means no department filter (admins).
    private const string ScopeSql = "(@scope::int IS NULL OR r.department_id = @scope)";

    public async Task<AlertCounts> GetCountsAsync(int? scopeDepartmentId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.QuerySingleAsync<AlertCounts>($@"
            SELECT
              COUNT(*) FILTER (WHERE {Open} AND r.assigned_to IS NULL)::int AS unassigned,
              COUNT(*) FILTER (WHERE {Open} AND r.assigned_to IS NULL AND {Triage})::int AS needs_triage,
              COUNT(*) FILTER (WHERE {Open} AND {Stale})::int AS stale,
              COUNT(*) FILTER (WHERE {Open})::int AS open
            FROM reports r
            JOIN departments d ON d.id = r.department_id
            WHERE r.archived_at IS NULL AND {ScopeSql}",
            new { scope = scopeDepartmentId });
    }

    /// <param name="kind">"unassigned", "triage" or "stale"</param>
    public async Task<List<AlertListItem>> ListAsync(string kind, int? scopeDepartmentId)
    {
        var condition = kind switch
        {
            "triage" => $"{Open} AND r.assigned_to IS NULL AND {Triage}",
            "stale" => $"{Open} AND {Stale}",
            _ => $"{Open} AND r.assigned_to IS NULL"
        };

        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<AlertListItem>($@"
            SELECT r.id, c.name_en AS category_name, r.address, r.status::text AS status,
                   d.name_en AS department_name,
                   NULLIF(TRIM(COALESCE(au.first_name, '') || ' ' || COALESCE(au.last_name, '')), '') AS assigned_name,
                   r.created_at, r.updated_at
            FROM reports r
            JOIN categories c ON c.id = r.category_id
            JOIN departments d ON d.id = r.department_id
            LEFT JOIN users au ON au.id = r.assigned_to
            WHERE r.archived_at IS NULL AND {ScopeSql} AND ({condition})
            ORDER BY r.created_at
            LIMIT 200",
            new { scope = scopeDepartmentId });
        return rows.ToList();
    }
}
