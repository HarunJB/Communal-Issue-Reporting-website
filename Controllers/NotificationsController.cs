using Microsoft.AspNetCore.Mvc;
using product_website.Data;

namespace product_website.Controllers;

[Route("api/notifications")]
public class NotificationsController : AuthedApiController
{
    private readonly NotificationRepository _notifications;

    public NotificationsController(NotificationRepository notifications) => _notifications = notifications;

    // GET /api/notifications
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _notifications.ListAsync(UserId, 100));

    // GET /api/notifications/unread-count
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount() =>
        Ok(new { count = await _notifications.UnreadCountAsync(UserId) });

    // POST /api/notifications/5/read
    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        await _notifications.MarkReadAsync(UserId, id);
        return NoContent();
    }

    // POST /api/notifications/read-all
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await _notifications.MarkAllReadAsync(UserId);
        return NoContent();
    }
}
