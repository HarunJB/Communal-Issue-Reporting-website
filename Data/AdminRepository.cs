using System.Text.Json;
using Dapper;
using Npgsql;
using product_website.Models;

namespace product_website.Data;

/// <summary>Admin-only management: users, categories, departments and the audit log.</summary>
public class AdminRepository
{
    private readonly NpgsqlDataSource _db;

    public AdminRepository(NpgsqlDataSource db) => _db = db;

    public const int PageSize = 25;

    private static Task AuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, int actorId, string action, string entityType, int? entityId, object details) =>
        conn.ExecuteAsync(@"
            INSERT INTO audit_log (actor_id, action, entity_type, entity_id, details)
            VALUES (@actorId, @action, @entityType, @entityId, CAST(@details AS jsonb))",
            new { actorId, action, entityType, entityId, details = JsonSerializer.Serialize(details) }, tx);

    // =================================================================
    // Users
    // =================================================================

    public async Task<UserListViewModel> ListUsersAsync(string? q, string? role, string? status, int page)
    {
        page = Math.Max(1, page);
        var where = new List<string>();
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var escaped = q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            where.Add("(u.email ILIKE @likeText OR u.first_name ILIKE @likeText OR u.last_name ILIKE @likeText)");
            p.Add("likeText", $"%{escaped}%");
        }
        if (role is "citizen" or "operator" or "admin")
        {
            where.Add("u.role = CAST(@role AS user_role)");
            p.Add("role", role);
        }
        if (status == "deleted")
            where.Add("u.deleted_at IS NOT NULL");
        else
        {
            where.Add("u.deleted_at IS NULL");
            if (status is "active" or "suspended")
            {
                where.Add("u.status = CAST(@status AS user_status)");
                p.Add("status", status);
            }
        }

        var whereSql = " WHERE " + string.Join(" AND ", where);
        p.Add("limit", PageSize);
        p.Add("offset", (page - 1) * PageSize);

        await using var conn = await _db.OpenConnectionAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*)::int FROM users u {whereSql}", p);

        var users = await conn.QueryAsync<UserRow>($@"
            SELECT u.id, u.email, u.first_name, u.last_name, u.role::text AS role, u.status::text AS status,
                   u.department_id, d.name_en AS department_name, u.created_at,
                   (u.deleted_at IS NOT NULL) AS is_deleted,
                   (SELECT COUNT(*)::int FROM reports r WHERE r.user_id = u.id) AS report_count,
                   CASE WHEN u.role = 'citizen' THEN u.trust_score END AS trust_score
            FROM users u
            LEFT JOIN departments d ON d.id = u.department_id
            {whereSql}
            ORDER BY CASE u.role WHEN 'admin' THEN 0 WHEN 'operator' THEN 1 ELSE 2 END, u.created_at DESC
            LIMIT @limit OFFSET @offset", p);

        return new UserListViewModel
        {
            Users = users.ToList(),
            Q = q,
            Role = role,
            Status = status,
            Page = page,
            Total = total,
            TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize))
        };
    }

    public async Task<UserRow?> GetUserAsync(int id)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<UserRow>(@"
            SELECT u.id, u.email, u.first_name, u.last_name, u.role::text AS role, u.status::text AS status,
                   u.department_id, d.name_en AS department_name, u.created_at,
                   (u.deleted_at IS NOT NULL) AS is_deleted, 0 AS report_count
            FROM users u LEFT JOIN departments d ON d.id = u.department_id
            WHERE u.id = @id", new { id });
    }

    public async Task<bool> EmailTakenAsync(string email, int exceptId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM users WHERE email = @email AND id <> @exceptId)", new { email, exceptId });
    }

    public async Task<int> CreateUserAsync(int actorId, UserFormModel m, string passwordHash)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var id = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO users (email, password_hash, first_name, last_name, display_name, role, department_id, language)
            VALUES (@Email, @passwordHash, @FirstName, @LastName, @FirstName, CAST(@Role AS user_role), @departmentId, 'bs')
            RETURNING id",
            new
            {
                Email = m.Email.Trim().ToLowerInvariant(),
                passwordHash,
                FirstName = m.FirstName.Trim(),
                LastName = m.LastName.Trim(),
                m.Role,
                departmentId = m.Role == "operator" ? m.DepartmentId : null
            }, tx);

        await AuditAsync(conn, tx, actorId, "user.created", "user", id, new { role = m.Role, email = m.Email.Trim().ToLowerInvariant() });
        await tx.CommitAsync();
        return id;
    }

    /// <summary>Updates name, email, role and department. A new password is set only if one is given.</summary>
    public async Task UpdateUserAsync(int actorId, UserFormModel m, string? newPasswordHash)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var before = await conn.QuerySingleOrDefaultAsync<UserRow>(
            "SELECT id, role::text AS role, department_id FROM users WHERE id = @id FOR UPDATE", new { m.Id }, tx);
        if (before is null) return;

        await conn.ExecuteAsync(@"
            UPDATE users
            SET first_name = @FirstName, last_name = @LastName,
                email = @Email,
                role = CAST(@Role AS user_role),
                department_id = @departmentId,
                password_hash = COALESCE(@newPasswordHash, password_hash),
                updated_at = now()
            WHERE id = @Id",
            new
            {
                m.Id,
                FirstName = m.FirstName.Trim(),
                LastName = m.LastName.Trim(),
                Email = m.Email.Trim().ToLowerInvariant(),
                m.Role,
                departmentId = m.Role == "operator" ? m.DepartmentId : null,
                newPasswordHash
            }, tx);

        await AuditAsync(conn, tx, actorId, "user.updated", "user", m.Id, new
        {
            roleFrom = before.Role,
            roleTo = m.Role,
            departmentFrom = before.DepartmentId,
            departmentTo = m.Role == "operator" ? m.DepartmentId : null,
            passwordReset = newPasswordHash is not null
        });
        await tx.CommitAsync();
    }

    public async Task SetUserStatusAsync(int actorId, int userId, string status)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync(@"
            UPDATE users SET status = CAST(@status AS user_status), updated_at = now()
            WHERE id = @userId", new { userId, status }, tx);
        await AuditAsync(conn, tx, actorId, status == "suspended" ? "user.suspended" : "user.reactivated", "user", userId, new { });
        await tx.CommitAsync();
    }

    /// <summary>True if at least one other active admin would remain.</summary>
    public async Task<bool> AnotherActiveAdminExistsAsync(int exceptUserId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (SELECT 1 FROM users
                           WHERE role = 'admin' AND status = 'active' AND deleted_at IS NULL AND id <> @exceptUserId)",
            new { exceptUserId });
    }

    // =================================================================
    // Departments
    // =================================================================

    public async Task<List<DepartmentRow>> ListDepartmentsAsync()
    {
        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<DepartmentRow>(@"
            SELECT d.id, d.name_en, d.name_bs, d.contact_email, d.is_active,
                   (SELECT COUNT(*)::int FROM users u
                     WHERE u.department_id = d.id AND u.role = 'operator' AND u.deleted_at IS NULL) AS operator_count,
                   (SELECT COUNT(*)::int FROM categories c WHERE c.department_id = d.id) AS category_count,
                   (SELECT COUNT(*)::int FROM reports r
                     WHERE r.department_id = d.id AND r.archived_at IS NULL
                       AND r.status IN ('submitted','in_review','assigned','in_progress')) AS open_reports
            FROM departments d
            ORDER BY d.is_active DESC, d.name_en");
        return rows.ToList();
    }

    public async Task<DepartmentFormModel?> GetDepartmentAsync(int id)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<DepartmentFormModel>(@"
            SELECT id, name_en, name_bs, contact_email, is_active FROM departments WHERE id = @id", new { id });
    }

    public async Task<int> SaveDepartmentAsync(int actorId, DepartmentFormModel m)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var email = string.IsNullOrWhiteSpace(m.ContactEmail) ? null : m.ContactEmail.Trim();
        int id;
        string action;
        if (m.Id == 0)
        {
            id = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO departments (name_en, name_bs, contact_email, is_active)
                VALUES (@NameEn, @NameBs, @email, @IsActive) RETURNING id",
                new { NameEn = m.NameEn.Trim(), NameBs = m.NameBs.Trim(), email, m.IsActive }, tx);
            action = "department.created";
        }
        else
        {
            id = m.Id;
            await conn.ExecuteAsync(@"
                UPDATE departments SET name_en = @NameEn, name_bs = @NameBs, contact_email = @email, is_active = @IsActive
                WHERE id = @Id",
                new { m.Id, NameEn = m.NameEn.Trim(), NameBs = m.NameBs.Trim(), email, m.IsActive }, tx);
            action = "department.updated";
        }

        await AuditAsync(conn, tx, actorId, action, "department", id, new { name = m.NameEn.Trim(), m.IsActive });
        await tx.CommitAsync();
        return id;
    }

    // =================================================================
    // Categories
    // =================================================================

    public async Task<List<CategoryRow>> ListCategoriesAsync()
    {
        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<CategoryRow>(@"
            SELECT c.id, c.name_en, c.name_bs, c.icon, c.department_id, d.name_en AS department_name,
                   c.default_priority::text AS default_priority, c.is_active,
                   (SELECT COUNT(*)::int FROM reports r WHERE r.category_id = c.id) AS report_count
            FROM categories c
            JOIN departments d ON d.id = c.department_id
            ORDER BY c.is_active DESC, d.name_en, c.name_en");
        return rows.ToList();
    }

    public async Task<CategoryFormModel?> GetCategoryAsync(int id)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<CategoryFormModel>(@"
            SELECT id, name_en, name_bs, icon, department_id, default_priority::text AS default_priority, is_active
            FROM categories WHERE id = @id", new { id });
    }

    public async Task<int> SaveCategoryAsync(int actorId, CategoryFormModel m)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var icon = string.IsNullOrWhiteSpace(m.Icon) ? null : m.Icon.Trim();
        int id;
        string action;
        if (m.Id == 0)
        {
            id = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO categories (name_en, name_bs, icon, department_id, default_priority, is_active)
                VALUES (@NameEn, @NameBs, @icon, @DepartmentId, CAST(@DefaultPriority AS priority_level), @IsActive)
                RETURNING id",
                new { NameEn = m.NameEn.Trim(), NameBs = m.NameBs.Trim(), icon, m.DepartmentId, m.DefaultPriority, m.IsActive }, tx);
            action = "category.created";
        }
        else
        {
            id = m.Id;
            await conn.ExecuteAsync(@"
                UPDATE categories
                SET name_en = @NameEn, name_bs = @NameBs, icon = @icon, department_id = @DepartmentId,
                    default_priority = CAST(@DefaultPriority AS priority_level), is_active = @IsActive
                WHERE id = @Id",
                new { m.Id, NameEn = m.NameEn.Trim(), NameBs = m.NameBs.Trim(), icon, m.DepartmentId, m.DefaultPriority, m.IsActive }, tx);
            action = "category.updated";
        }

        await AuditAsync(conn, tx, actorId, action, "category", id, new { name = m.NameEn.Trim(), m.DepartmentId, m.IsActive });
        await tx.CommitAsync();
        return id;
    }

    // =================================================================
    // Audit log
    // =================================================================

    public async Task<AuditViewModel> GetAuditAsync(string? action, string? entityType, int page)
    {
        page = Math.Max(1, page);
        var where = new List<string>();
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(action))
        {
            where.Add("a.action = @action");
            p.Add("action", action);
        }
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            where.Add("a.entity_type = @entityType");
            p.Add("entityType", entityType);
        }
        var whereSql = where.Count == 0 ? "" : " WHERE " + string.Join(" AND ", where);
        p.Add("limit", PageSize);
        p.Add("offset", (page - 1) * PageSize);

        await using var conn = await _db.OpenConnectionAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*)::int FROM audit_log a {whereSql}", p);

        var rows = await conn.QueryAsync<AuditRow>($@"
            SELECT a.id, NULLIF(TRIM(CONCAT(u.first_name, ' ', u.last_name)), '') AS actor_name,
                   a.action, a.entity_type, a.entity_id, a.details::text AS details, a.created_at
            FROM audit_log a
            LEFT JOIN users u ON u.id = a.actor_id
            {whereSql}
            ORDER BY a.created_at DESC, a.id DESC
            LIMIT @limit OFFSET @offset", p);

        var actions = await conn.QueryAsync<string>("SELECT DISTINCT action FROM audit_log ORDER BY action");

        return new AuditViewModel
        {
            Rows = rows.ToList(),
            Action = action,
            EntityType = entityType,
            Actions = actions.ToList(),
            Page = page,
            Total = total,
            TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize))
        };
    }
}
