using Dapper;
using Npgsql;

namespace product_website.Data;

public static class DbSeeder
{
    private const string DemoPassword = "Demo12345!";

    public static async Task SeedAsync(NpgsqlDataSource db)
    {
        await using var conn = await db.OpenConnectionAsync();

        // An operator must belong to a department 
        var departmentId = await conn.ExecuteScalarAsync<int?>(
            "SELECT id FROM departments WHERE name_en = @name",
            new { name = "Water and Sewage Company" });

        if (departmentId is null)
        {
            departmentId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO departments (name_bs, name_en, contact_email)
                VALUES (@bs, @en, @mail) RETURNING id",
                new { bs = "Vodovod i Kanalizacija", en = "Water and Sewage Company", mail = "info@example.ba" });
        }

        await EnsureUserAsync(conn, "citizen@demo.ba", "Demo", "Citizen", "citizen", null);
        await EnsureUserAsync(conn, "operator@demo.ba", "Demo", "Operator", "operator", departmentId);
        await EnsureUserAsync(conn, "admin@demo.ba", "Demo", "Admin", "admin", null);
    }

    private static async Task EnsureUserAsync(
        NpgsqlConnection conn, string email, string firstName, string lastName, string role, int? departmentId)
    {
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM users WHERE email = @email)", new { email });
        if (exists) return;

        await conn.ExecuteAsync(@"
            INSERT INTO users (email, password_hash, first_name, last_name, display_name, role, department_id)
            VALUES (@email, @hash, @firstName, @lastName, @firstName, CAST(@role AS user_role), @departmentId)",
            new
            {
                email,
                hash = BCrypt.Net.BCrypt.HashPassword(DemoPassword),
                firstName,
                lastName,
                role,
                departmentId
            });
    }
}
