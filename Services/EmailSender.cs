using System.Net.Http.Json;

namespace product_website.Services;

/// <summary>
/// Sends transactional email through Brevo's HTTPS API (no SMTP, so it works on Render).
/// Settings: Email__BrevoApiKey, Email__SenderEmail (must be verified in Brevo), Email__SenderName.
/// Without an API key (local development) nothing is sent: the code is written to the console instead.
/// </summary>
public class EmailSender
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailSender> _log;

    public EmailSender(IHttpClientFactory http, IConfiguration config, ILogger<EmailSender> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    public async Task<bool> SendCodeAsync(string toEmail, string toName, string purpose, string code)
    {
        var isReset = purpose == "reset";
        var subject = isReset ? "Your password reset code" : "Verify your email address";
        var intro = isReset
            ? "Use this code to choose a new password for Sarajevo Reports:"
            : "Welcome to Sarajevo Reports. Use this code to verify your email address:";

        var html = $@"<div style=""font-family:Arial,sans-serif;max-width:480px"">
<h2 style=""color:#17324d"">Sarajevo Reports</h2>
<p>Hello {System.Net.WebUtility.HtmlEncode(toName)},</p>
<p>{intro}</p>
<p style=""font-size:32px;letter-spacing:6px;font-weight:bold;color:#17324d"">{code}</p>
<p>The code is valid for 15 minutes. If you did not ask for it, you can ignore this email.</p>
</div>";

        var apiKey = _config["Email:BrevoApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _log.LogWarning("EMAIL (not sent, no Brevo key set) to {Email}: {Subject} - code {Code}", toEmail, subject, code);
            return true;
        }

        var senderEmail = _config["Email:SenderEmail"];
        if (string.IsNullOrWhiteSpace(senderEmail))
        {
            _log.LogError("Email:SenderEmail is not set.");
            return false;
        }

        try
        {
            var client = _http.CreateClient("brevo");
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
            request.Headers.Add("api-key", apiKey);
            request.Content = JsonContent.Create(new
            {
                sender = new { name = _config["Email:SenderName"] ?? "Sarajevo Reports", email = senderEmail },
                to = new[] { new { email = toEmail, name = toName } },
                subject,
                htmlContent = html
            });

            using var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode) return true;

            _log.LogError("Brevo refused the email ({Status}): {Body}", (int)response.StatusCode,
                await response.Content.ReadAsStringAsync());
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Sending email failed.");
            return false;
        }
    }
}
