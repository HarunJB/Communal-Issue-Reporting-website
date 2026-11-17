using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using product_website.Data;

namespace product_website.Controllers;

public class DeviceRequest
{
    [Required, StringLength(4096)]
    public string Token { get; set; } = string.Empty;

    [RegularExpression("^(android|ios|web)$")]
    public string Platform { get; set; } = "android";
}

/// <summary>The app tells the server which phone to send push notifications to.</summary>
[Route("api/devices")]
public class DevicesController : AuthedApiController
{
    private readonly DeviceTokenRepository _tokens;

    public DevicesController(DeviceTokenRepository tokens) => _tokens = tokens;

    // POST /api/devices   body: { "token": "...", "platform": "android" }
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] DeviceRequest request)
    {
        await _tokens.SaveAsync(UserId, request.Token, request.Platform);
        return NoContent();
    }

    // POST /api/devices/remove   body: { "token": "..." }   (on sign out: stop pushes to this phone)
    [HttpPost("remove")]
    public async Task<IActionResult> Remove([FromBody] DeviceRequest request)
    {
        await _tokens.RemoveAsync(UserId, request.Token);
        return NoContent();
    }
}
