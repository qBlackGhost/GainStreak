using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GymApp.Contracts;

namespace GymApp.Server.Push;

/// <summary>
/// iPhone pushes through Apple's push service (APNs), over HTTP/2 with token auth: an ES256 JWT signed with the .p8 key,
/// which .NET signs itself, so no Apple SDK. The JWT is reused for <see cref="TokenLifetime"/> (Apple refuses one older
/// than an hour and dislikes new ones more often than every 20 minutes). Each device goes to the sandbox or the production
/// host by its <see cref="PushTarget.Sandbox"/>. Never logs a device token: it's in the URL, so the HTTP client has no
/// request logging either.
/// </summary>
public sealed class ApnsSender(ApnsSettings settings, IHttpClientFactory http, TimeProvider clock, ILogger<ApnsSender> log) : IPushSender
{
    public const string HttpClientName = "apns";
    public const string ProductionHost = "api.push.apple.com";
    public const string SandboxHost = "api.sandbox.push.apple.com";
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(40);
    public static readonly TimeSpan Expiry = TimeSpan.FromDays(1);

    private readonly Lock _lock = new();
    private (string Jwt, DateTimeOffset MadeAt)? _jwt;

    public PushPlatform Platform => PushPlatform.Apns;

    public async Task<PushSendResult> SendAsync(PushTarget target, PushMessage message, CancellationToken ct)
    {
        var (status, reason) = await PostAsync(target, message, CurrentToken(renew: false), ct);
        if (status == HttpStatusCode.Forbidden && reason == "ExpiredProviderToken")
            (status, reason) = await PostAsync(target, message, CurrentToken(renew: true), ct);

        if (status == HttpStatusCode.OK) return PushSendResult.Sent;
        if (status == HttpStatusCode.Gone || reason is "BadDeviceToken" or "DeviceTokenNotForTopic" or "Unregistered")
            return PushSendResult.DeadToken;
        log.LogWarning("APNs refused a push ({Sandbox}): {Status} {Reason}.",
            target.Sandbox ? "sandbox" : "production", (int)status, reason);
        return PushSendResult.Failed;
    }

    private async Task<(HttpStatusCode Status, string? Reason)> PostAsync(PushTarget target, PushMessage message, string jwt, CancellationToken ct)
    {
        using var request = BuildRequest(target, message, jwt, settings.BundleId, clock.GetUtcNow());
        using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return (HttpStatusCode.OK, null);
        string? reason = null;
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("reason", out var r)) reason = r.GetString();
        }
        catch (JsonException) { }
        return (response.StatusCode, reason);
    }

    private string CurrentToken(bool renew)
    {
        var now = clock.GetUtcNow();
        lock (_lock)
        {
            if (renew || _jwt is not { } jwt || now - jwt.MadeAt >= TokenLifetime || now < jwt.MadeAt)
                _jwt = (CreateToken(settings, now), now);
            return _jwt.Value.Jwt;
        }
    }

    /// <summary>The provider token: <c>{"alg":"ES256","kid":…}.{"iss":team,"iat":…}</c>, signed with the .p8 key.</summary>
    public static string CreateToken(ApnsSettings settings, DateTimeOffset now)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = settings.KeyId }));
        var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { iss = settings.TeamId, iat = now.ToUnixTimeSeconds() }));
        var signed = $"{header}.{claims}";
        using var key = ECDsa.Create();
        key.ImportFromPem(settings.PrivateKeyPem);
        // JWS wants the raw r||s form, which is .NET's default for SignData
        var signature = key.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{signed}.{Base64Url(signature)}";
    }

    public static HttpRequestMessage BuildRequest(PushTarget target, PushMessage message, string jwt, string bundleId, DateTimeOffset now)
    {
        var host = target.Sandbox ? SandboxHost : ProductionHost;
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/3/device/{target.Token}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new StringContent(BuildBody(message), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", jwt);
        request.Headers.Add("apns-topic", bundleId);
        request.Headers.Add("apns-push-type", "alert");
        request.Headers.Add("apns-priority", "10");
        request.Headers.Add("apns-expiration", (now + Expiry).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("apns-collapse-id", message.CollapseKey);
        return request;
    }

    /// <summary>The alert, grouped as "friends", plus the data for the tap at the top level.</summary>
    public static string BuildBody(PushMessage message)
    {
        var body = new Dictionary<string, object>
        {
            ["aps"] = new Dictionary<string, object>
            {
                ["alert"] = new { title = message.Title, body = message.Body },
                ["sound"] = "default",
                ["thread-id"] = "friends"
            }
        };
        foreach (var (key, value) in message.Data) body[key] = value;
        return JsonSerializer.Serialize(body);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
