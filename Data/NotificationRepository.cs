using Dapper;
using Npgsql;
using product_website.Models;

namespace product_website.Data;

public class NotificationRepository
{
    private readonly NpgsqlDataSource _db;

    public NotificationRepository(NpgsqlDataSource db) => _db = db;

    public async Task<List<NoticeDto>> ListAsync(int userId, int limit)
    {
        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<NoticeDto>(@"
            SELECT id, report_id, title, body, is_read, created_at
            FROM notifications
            WHERE user_id = @userId
            ORDER BY created_at DESC, id DESC
            LIMIT @limit",
            new { userId, limit });
        return rows.ToList();
    }

    public async Task<int> UnreadCountAsync(int userId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM notifications WHERE user_id = @userId AND NOT is_read",
            new { userId });
    }

    public async Task MarkReadAsync(int userId, int id)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE notifications SET is_read = TRUE WHERE id = @id AND user_id = @userId",
            new { id, userId });
    }

    public async Task MarkAllReadAsync(int userId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE notifications SET is_read = TRUE WHERE user_id = @userId AND NOT is_read",
            new { userId });
    }
}
