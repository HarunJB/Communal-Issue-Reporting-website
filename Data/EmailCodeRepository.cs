using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;

namespace product_website.Data;

/// <summary>One-time email codes (verify email, reset password) and the "verified" flag.</summary>
public class EmailCodeRepository
{
    private const int MaxAttempts = 5;
    private const int ValidMinutes = 15;
    private const int CooldownSeconds = 60;

    private readonly NpgsqlDataSource _db;

    public EmailCodeRepository(NpgsqlDataSource db) => _db = db;

    public async Task<bool> IsVerifiedAsync(int userId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT email_verified_at IS NOT NULL FROM users WHERE id = @userId", new { userId });
    }

    public async Task MarkUnverifiedAsync(int userId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync("UPDATE users SET email_verified_at = NULL WHERE id = @userId", new { userId });
    }

    public async Task MarkVerifiedAsync(int userId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET email_verified_at = COALESCE(email_verified_at, now()) WHERE id = @userId", new { userId });
    }

    /// <summary>Makes a new 6 digit code (older unused ones stop working). Returns null while the 60 second cooldown runs.</summary>
    public async Task<string?> CreateAsync(int userId, string purpose)
    {
        await using var conn = await _db.OpenConnectionAsync();

        var recent = await conn.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (SELECT 1 FROM email_codes
                           WHERE user_id = @userId AND purpose = @purpose
                             AND created_at > now() - make_interval(secs => @cooldown))",
            new { userId, purpose, cooldown = CooldownSeconds });
        if (recent) return null;

        await conn.ExecuteAsync(
            "UPDATE email_codes SET used_at = now() WHERE user_id = @userId AND purpose = @purpose AND used_at IS NULL",
            new { userId, purpose });

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        await conn.ExecuteAsync(@"
            INSERT INTO email_codes (user_id, purpose, code_hash, expires_at)
            VALUES (@userId, @purpose, @hash, now() + make_interval(mins => @minutes))",
            new { userId, purpose, hash = Hash(userId, purpose, code), minutes = ValidMinutes });
        return code;
    }

    /// <summary>Checks a code. Wrong codes are counted; after 5 wrong tries the code is dead.</summary>
    public async Task<(bool Ok, string? Error)> CheckAsync(int userId, string purpose, string code)
    {
        await using var conn = await _db.OpenConnectionAsync();

        var row = await conn.QuerySingleOrDefaultAsync<CodeRow>(@"
            SELECT id, code_hash AS CodeHash, attempts
            FROM email_codes
            WHERE user_id = @userId AND purpose = @purpose AND used_at IS NULL AND expires_at > now()
            ORDER BY created_at DESC LIMIT 1",
            new { userId, purpose });

        const string expired = "This code is wrong or has expired. Please ask for a new one.";
        if (row is null) return (false, expired);

        if (row.Attempts >= MaxAttempts)
        {
            await conn.ExecuteAsync("UPDATE email_codes SET used_at = now() WHERE id = @id", new { id = row.Id });
            return (false, "Too many wrong tries. Please ask for a new code.");
        }

        var given = Encoding.UTF8.GetBytes(Hash(userId, purpose, code.Trim()));
        var stored = Encoding.UTF8.GetBytes(row.CodeHash);
        if (!CryptographicOperations.FixedTimeEquals(given, stored))
        {
            await conn.ExecuteAsync("UPDATE email_codes SET attempts = attempts + 1 WHERE id = @id", new { id = row.Id });
            return (false, expired);
        }

        await conn.ExecuteAsync("UPDATE email_codes SET used_at = now() WHERE id = @id", new { id = row.Id });
        return (true, null);
    }

    private sealed class CodeRow
    {
        public int Id { get; set; }
        public string CodeHash { get; set; } = string.Empty;
        public int Attempts { get; set; }
    }

    private static string Hash(int userId, string purpose, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{purpose}:{code}"))).ToLowerInvariant();
}
