using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using GymApp.Contracts;

namespace GymApp.Server.Push;

/// <summary>An OAuth access token for FCM. The real one is <see cref="GoogleFcmAccessTokens"/>; the tests use a fake.</summary>
public interface IFcmAccessTokens
{
    Task<string> GetAsync(CancellationToken ct);
}

/// <summary>Access tokens from the Firebase service account, through Google's own auth library, which caches each
/// token and gets a new one before it expires (they last an hour).</summary>
public sealed class GoogleFcmAccessTokens(FcmSettings settings) : IFcmAccessTokens
{
    public const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly ITokenAccess _credential = CredentialFactory
        .FromJson<ServiceAccountCredential>(settings.ServiceAccountJson)
        .ToGoogleCredential()
        .CreateScoped(Scope);

    public Task<string> GetAsync(CancellationToken ct) => _credential.GetAccessTokenForRequestAsync(cancellationToken: ct);
}

/// <summary>
/// Android pushes through Firebase Cloud Messaging's HTTP v1 API. A notification message, so Android shows it itself
/// while the app is in the background (on the "friends" channel, with the app's small icon), plus the data for the tap.
/// A token FCM no longer knows, or that belongs to another Firebase project, is reported dead. Never logs a token.
/// </summary>
public sealed class FcmSender(FcmSettings settings, IFcmAccessTokens tokens, IHttpClientFactory http, ILogger<FcmSender> log) : IPushSender
{
    public const string HttpClientName = "fcm";
    public const string ChannelId = "friends";
    public const string Icon = "ic_notification";
    public static readonly TimeSpan Expiry = TimeSpan.FromDays(1);

    public PushPlatform Platform => PushPlatform.Fcm;

    public Uri SendUrl => new($"https://fcm.googleapis.com/v1/projects/{settings.ProjectId}/messages:send");

    public async Task<PushSendResult> SendAsync(PushTarget target, PushMessage message, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SendUrl)
        {
            Content = new StringContent(BuildBody(target, message), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(ct));
        using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return PushSendResult.Sent;

        var (code, text) = ReadError(await response.Content.ReadAsStringAsync(ct));
        if (IsDeadToken(response.StatusCode, code, text)) return PushSendResult.DeadToken;
        // FCM's message names the problem ("Invalid data payload key: …") and never echoes the device token
        log.LogWarning("FCM refused a push: {Status} {Code}: {Message}", (int)response.StatusCode, code, text);
        return PushSendResult.Failed;
    }

    /// <summary>404 UNREGISTERED (the app was removed or the token replaced), SENDER_ID_MISMATCH (another project's
    /// token), or a 400 that says the registration token itself isn't valid.</summary>
    public static bool IsDeadToken(HttpStatusCode status, string? code, string? text) =>
        code is "UNREGISTERED" or "SENDER_ID_MISMATCH"
        || (status == HttpStatusCode.BadRequest && text?.Contains("registration token", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>FCM's own error code from <c>error.details[].errorCode</c> (else <c>error.status</c>), and the message.</summary>
    public static (string? Code, string? Message) ReadError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var error)) return (null, null);
            string? code = null;
            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                foreach (var d in details.EnumerateArray())
                    if (d.TryGetProperty("errorCode", out var c)) code ??= c.GetString();
            if (code is null && error.TryGetProperty("status", out var s)) code = s.GetString();
            return (code, error.TryGetProperty("message", out var m) ? m.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    public static string BuildBody(PushTarget target, PushMessage message) => JsonSerializer.Serialize(new
    {
        message = new
        {
            token = target.Token,
            notification = new { title = message.Title, body = message.Body },
            data = message.Data,
            android = new
            {
                priority = "high",
                ttl = $"{(long)Expiry.TotalSeconds}s",
                collapse_key = message.CollapseKey,
                notification = new { channel_id = ChannelId, tag = message.CollapseKey, icon = Icon }
            }
        }
    });
}
