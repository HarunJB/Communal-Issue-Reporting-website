using Microsoft.AspNetCore.Mvc;
using product_website.Data;
using product_website.Models;
using product_website.Services;

namespace product_website.Controllers;

[Route("api/me")]
public class MeController : AuthedApiController
{
    private readonly UserRepository _users;
    private readonly PhotoStorage _photos;

    public MeController(UserRepository users, PhotoStorage photos)
    {
        _users = users;
        _photos = photos;
    }

    private async Task<UserDto> ToDtoAsync(User user) =>
        UserDto.From(user) with { AvatarUrl = await _users.GetAvatarUrlAsync(user.Id) };

    // GET /api/me
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = await _users.GetByIdAsync(UserId);
        return user is null ? Unauthorized() : Ok(await ToDtoAsync(user));
    }

    // PUT /api/me
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateProfileRequest request)
    {
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        var user = await _users.UpdateProfileAsync(
            UserId, request.FirstName.Trim(), request.LastName.Trim(), displayName, phone, request.Language);

        return user is null ? Unauthorized() : Ok(await ToDtoAsync(user));
    }

    // POST /api/me/avatar   (multipart form, one file in the field "photo")
    [HttpPost("avatar")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> SetAvatar(IFormFile photo, CancellationToken ct)
    {
        if (photo.Length == 0 || photo.Length > PhotoStorage.MaxBytes)
            return BadRequest(new { message = "The photo must be smaller than 8 MB." });

        var extension = await PhotoStorage.DetectExtensionAsync(photo, ct);
        if (extension is null)
            return BadRequest(new { message = "Only JPEG, PNG or WebP photos are allowed." });

        var user = await _users.GetByIdAsync(UserId);
        if (user is null) return Unauthorized();

        var oldUrl = await _users.GetAvatarUrlAsync(UserId);
        var newUrl = await _photos.SaveAsync(photo, extension, ct, "avatars");

        try
        {
            await _users.SetAvatarUrlAsync(UserId, newUrl);
        }
        catch
        {
            _photos.Delete(newUrl);
            throw;
        }

        if (!string.IsNullOrEmpty(oldUrl)) _photos.Delete(oldUrl);
        return Ok(await ToDtoAsync(user));
    }

    // DELETE /api/me/avatar
    [HttpDelete("avatar")]
    public async Task<IActionResult> RemoveAvatar()
    {
        var user = await _users.GetByIdAsync(UserId);
        if (user is null) return Unauthorized();

        var oldUrl = await _users.GetAvatarUrlAsync(UserId);
        await _users.SetAvatarUrlAsync(UserId, null);
        if (!string.IsNullOrEmpty(oldUrl)) _photos.Delete(oldUrl);

        return Ok(await ToDtoAsync(user));
    }

    // POST /api/me/password
    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var user = await _users.GetByIdAsync(UserId);
        if (user is null) return Unauthorized();

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "Your current password is not correct." });

        await _users.UpdatePasswordAsync(UserId, BCrypt.Net.BCrypt.HashPassword(request.NewPassword));
        return NoContent();
    }

    // POST /api/me/delete   (body: { "password": "..." })
    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteAccountRequest request)
    {
        var user = await _users.GetByIdAsync(UserId);
        if (user is null) return Unauthorized();

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return BadRequest(new { message = "Your password is not correct." });

        await _users.AnonymizeAsync(UserId);
        return NoContent();
    }
}
