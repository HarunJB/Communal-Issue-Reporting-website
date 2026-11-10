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

    /// <summary>Updates the profile fields a user may change. Returns the updated user.</summary>
    public async Task<User?> UpdateProfileAsync(
        int id, string firstName, string lastName, string? displayName, string? phone, string language)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<User>($@"
            UPDATE users
            SET first_name = @firstName,
                last_name = @lastName,
                display_name = @displayName,
                phone = @phone,
                language = @language,
                updated_at = now()
            WHERE id = @id AND deleted_at IS NULL
            RETURNING {SelectColumns}",
            new { id, firstName, lastName, displayName, phone, language });
    }

    /// <summary>The department an operator belongs to (null for citizens and admins).</summary>
    public async Task<int?> GetDepartmentIdAsync(int id)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int?>(
            "SELECT department_id FROM users WHERE id = @id AND deleted_at IS NULL", new { id });
    }

    public async Task<string?> GetAvatarUrlAsync(int id)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string?>(
            "SELECT avatar_url FROM users WHERE id = @id AND deleted_at IS NULL", new { id });
    }

    public async Task SetAvatarUrlAsync(int id, string? url)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET avatar_url = @url, updated_at = now() WHERE id = @id", new { id, url });
    }

    public async Task UpdatePasswordAsync(int id, string passwordHash)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET password_hash = @passwordHash, updated_at = now() WHERE id = @id",
            new { id, passwordHash });
    }

    /// <summary>
    /// "Delete account": removes personal data but keeps the row, so the user's reports stay in the
    /// statistics. The email is freed, so the same address can register again later.
    /// </summary>
    public async Task AnonymizeAsync(int id)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync("DELETE FROM device_tokens WHERE user_id = @id", new { id }, tx);
        await conn.ExecuteAsync("DELETE FROM notifications WHERE user_id = @id", new { id }, tx);
        await conn.ExecuteAsync(@"
            UPDATE users
            SET email = @email,
                password_hash = '!',
                first_name = 'Deleted',
                last_name = 'User',
                display_name = 'Deleted user',
                phone = NULL,
                avatar_url = NULL,
                deleted_at = now(),
                updated_at = now()
            WHERE id = @id",
            new { id, email = $"deleted-{id}@deleted.invalid" }, tx);

        await tx.CommitAsync();
    }
}
