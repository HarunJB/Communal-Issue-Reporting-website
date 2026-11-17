using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using System.Text;

namespace product_website.Services;

/// <summary>
/// Sends push notifications through Firebase Cloud Messaging (FCM).
/// If no Firebase key is configured, push is simply switched off and the rest of the backend works as before.
/// Configure ONE of these:
///   on your PC:  dotnet user-secrets set "Firebase:CredentialsPath" "C:\secrets\firebase-key.json"
///   on the live site (no file there): Firebase:CredentialsJson = the key file's content, either as plain JSON
///   or, easier to paste into a setting, as Base64 (see DEPLOY.md).
/// </summary>
public class PushSender
{
    private readonly FirebaseMessaging? _messaging;
    private readonly ILogger<PushSender> _logger;

    public bool Enabled => _messaging is not null;

    public PushSender(IConfiguration configuration, ILogger<PushSender> logger)
    {
        _logger = logger;

        var json = configuration["Firebase:CredentialsJson"];
        var path = configuration["Firebase:CredentialsPath"];

        GoogleCredential credential;
        try
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                json = json.Trim();
                if (!json.StartsWith('{'))
                    json = Encoding.UTF8.GetString(Convert.FromBase64String(json));
                credential = GoogleCredential.FromJson(json);
            }
            else if (string.IsNullOrWhiteSpace(path))
            {
                _logger.LogWarning("Push notifications are OFF: neither Firebase:CredentialsJson nor Firebase:CredentialsPath is set.");
                return;
            }
            else if (!File.Exists(path))
            {
                _logger.LogWarning("Push notifications are OFF: the Firebase key file was not found at {Path}.", path);
                return;
            }
            else
            {
                credential = GoogleCredential.FromFile(path);
            }

            var app = FirebaseApp.DefaultInstance
                      ?? FirebaseApp.Create(new AppOptions { Credential = credential });
            _messaging = FirebaseMessaging.GetMessaging(app);
            _logger.LogInformation("Push notifications are ON (Firebase).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Push notifications are OFF: could not start Firebase.");
        }
    }

    /// <summary>Sends one message to each device token. Returns the tokens that no longer work (app removed, etc.).</summary>
    public async Task<List<string>> SendAsync(
        IEnumerable<string> tokens, string title, string? body, int? reportId, CancellationToken ct)
    {
        var dead = new List<string>();
        if (_messaging is null) return dead;

        foreach (var token in tokens)
        {
            var message = new Message
            {
                Token = token,
                Notification = new Notification { Title = title, Body = body },
                Data = new Dictionary<string, string>
                {
                    ["reportId"] = reportId?.ToString() ?? "",
                    ["type"] = "report_update"
                },
                Android = new AndroidConfig { Priority = Priority.High }
            };

            try
            {
                await _messaging.SendAsync(message, ct);
            }
            catch (FirebaseMessagingException ex)
                when (ex.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
            {
                dead.Add(token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not send a push notification.");
            }
        }
        return dead;
    }
}
