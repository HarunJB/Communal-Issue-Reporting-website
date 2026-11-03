using Dapper;
using Npgsql;
using product_website.Models;

namespace product_website.Data;

public class UserRepository
{
    private readonly NpgsqlDataSource _db;

    public UserRepository(NpgsqlDataSource db) => _db = db;

    private const string SelectColumns = @"
        id, email, password_hash, first_name, last_name, display_name, phone,
        role::text AS role, status::text AS status, department_id, language";

    /// <summary>Finds an active (not soft-deleted) user by email. Email must already be lower-case.</summary>
    public async Task<User?> GetByEmailAsync(string email)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<User>(
            $"SELECT {SelectColumns} FROM users WHERE email = @email AND deleted_at IS NULL",
            new { email });
    }

    /// <summary>Finds an active (not soft-deleted) user by id.</summary>
    public async Task<User?> GetByIdAsync(int id)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<User>(
            $"SELECT {SelectColumns} FROM users WHERE id = @id AND deleted_at IS NULL",
            new { id });
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM users WHERE email = @email)",
            new { email });
    }

    /// <summary>Creates a citizen account. Returns the new user id.</summary>
    public async Task<int> CreateCitizenAsync(
        string email, string passwordHash, string firstName, string lastName, string? phone, string language)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO users (email, password_hash, first_name, last_name, display_name, phone, role, language)
            VALUES (@email, @passwordHash, @firstName, @lastName, @firstName, @phone, 'citizen', @language)
            RETURNING id",
            new { email, passwordHash, firstName, lastName, phone, language });
    }
}
