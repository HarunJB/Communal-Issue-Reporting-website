using Dapper;
using Npgsql;
using product_website.Data;

namespace product_website.Services;

/// <summary>
/// Background worker: every few seconds it looks for notifications that were created but not pushed yet
/// (status changes, staff replies, ...) and sends them to the user's phones. Because it reads the same
/// "notifications" table as the in-app list, every event that creates a notice also gets a push,
/// without changing any of the code that creates notices.
/// </summary>
public class PushDispatcher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(4);

    // Notices older than this are not pushed (for example after the backend was off for a while).
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    private sealed class PendingNotice
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int? ReportId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Body { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private readonly NpgsqlDataSource _db;
    private readonly DeviceTokenRepository _tokens;
    private readonly PushSender _sender;
    private readonly ILogger<PushDispatcher> _logger;

    public PushDispatcher(
        NpgsqlDataSource db, DeviceTokenRepository tokens, PushSender sender, ILogger<PushDispatcher> logger)
    {
        _db = db;
        _tokens = tokens;
        _sender = sender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a moment so the database seeder finishes first.
        try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        try { await EnsureColumnAsync(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Push dispatcher could not prepare the database. Push is off.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push dispatcher round failed; trying again soon.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Adds notifications.pushed_at the first time. Existing notices count as already pushed.</summary>
    private async Task EnsureColumnAsync()
    {
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync(@"
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'notifications' AND column_name = 'pushed_at') THEN
                    ALTER TABLE notifications ADD COLUMN pushed_at TIMESTAMPTZ;
                    UPDATE notifications SET pushed_at = now();
                END IF;
            END $$;");
    }

    private async Task DispatchOnceAsync(CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);

        var pending = (await conn.QueryAsync<PendingNotice>(@"
            SELECT id, user_id, report_id, title, body, created_at
            FROM notifications
            WHERE pushed_at IS NULL
            ORDER BY id
            LIMIT 50")).ToList();

        foreach (var notice in pending)
        {
            var fresh = DateTime.UtcNow - DateTime.SpecifyKind(notice.CreatedAt, DateTimeKind.Utc) < MaxAge;

            if (_sender.Enabled && fresh)
            {
                var tokens = await _tokens.ListForUserAsync(notice.UserId);
                if (tokens.Count > 0)
                {
                    var dead = await _sender.SendAsync(tokens, notice.Title, notice.Body, notice.ReportId, ct);
                    await _tokens.RemoveManyAsync(dead);
                }
            }

            // Mark it done even when there was nothing to send, so it is never retried forever.
            await conn.ExecuteAsync(
                "UPDATE notifications SET pushed_at = now() WHERE id = @id", new { id = notice.Id });
        }
    }
}
