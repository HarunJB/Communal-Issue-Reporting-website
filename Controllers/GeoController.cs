using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using product_website.Services;

namespace product_website.Controllers;

[Route("api/geo")]
public class GeoController : AuthedApiController
{
    private static readonly SemaphoreSlim Throttle = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;

    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;

    public GeoController(IHttpClientFactory http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
    }

    // GET /api/geo/reverse?lat=43.85&lng=18.41  ->  { "address": "Ferhadija 5" }  (address may be null)
    // Done on the server because the Nominatim usage policy needs a User-Agent and max ~1 request/second.
    [HttpGet("reverse")]
    public async Task<IActionResult> Reverse([FromQuery] double lat, [FromQuery] double lng, CancellationToken ct)
    {
        if (!Geo.IsInSarajevo(lat, lng))
            return BadRequest(new { message = "The location must be inside Sarajevo Canton." });

        var latText = lat.ToString("F5", CultureInfo.InvariantCulture);
        var lngText = lng.ToString("F5", CultureInfo.InvariantCulture);
        var key = $"geo:{latText}:{lngText}";

        if (_cache.TryGetValue(key, out string? cached))
            return Ok(new { address = cached });

        string? address = null;
        await Throttle.WaitAsync(ct);
        try
        {
            var wait = TimeSpan.FromMilliseconds(1100) - (DateTime.UtcNow - _lastCall);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

            try
            {
                var client = _http.CreateClient("nominatim");
                var url = $"reverse?format=jsonv2&zoom=18&addressdetails=1&accept-language=bs,en&lat={latText}&lon={lngText}";
                using var response = await client.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    address = BuildAddress(doc.RootElement);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // Address lookup is a convenience; the app lets the user type it instead.
                address = null;
            }
            finally
            {
                _lastCall = DateTime.UtcNow;
            }
        }
        finally
        {
            Throttle.Release();
        }

        if (address is not null)
            _cache.Set(key, address, TimeSpan.FromHours(12));

        return Ok(new { address });
    }

    private static string? BuildAddress(JsonElement root)
    {
        if (!root.TryGetProperty("address", out var parts) || parts.ValueKind != JsonValueKind.Object)
            return null;

        string? Get(string name) =>
            parts.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var street = Get("road") ?? Get("pedestrian") ?? Get("footway") ?? Get("path");
        var number = Get("house_number");
        var area = Get("suburb") ?? Get("neighbourhood") ?? Get("city_district") ?? Get("quarter");

        if (street is null)
            return area;

        var line = number is null ? street : $"{street} {number}";
        return area is null ? line : $"{line}, {area}";
    }
}
