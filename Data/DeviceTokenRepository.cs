using Dapper;
using Npgsql;

namespace product_website.Data;

/// <summary>The push tokens of the phones a user is signed in on.</summary>
public class DeviceTokenRepository
{
    private readonly NpgsqlDataSource _db;

    public DeviceTokenRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>Saves a token for a user. If the token was used by another account on this phone, it moves.</summary>
    public async Task SaveAsync(int userId, string token, string platform)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO device_tokens (user_id, token, platform)
            VALUES (@userId, @token, CAST(@platform AS device_platform))
            ON CONFLICT (token) DO UPDATE SET user_id = EXCLUDED.user_id, platform = EXCLUDED.platform",
            new { userId, token, platform });
    }

    public async Task RemoveAsync(int userId, string token)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "DELETE FROM device_tokens WHERE token = @token AND user_id = @userId", new { userId, token });
    }

    public async Task RemoveManyAsync(IEnumerable<string> tokens)
    {
        var list = tokens.ToArray();
        if (list.Length == 0) return;
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM device_tokens WHERE token = ANY(@list)", new { list });
    }

    public async Task<List<string>> ListForUserAsync(int userId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<string>(
            "SELECT token FROM device_tokens WHERE user_id = @userId", new { userId });
        return rows.ToList();
    }
}
