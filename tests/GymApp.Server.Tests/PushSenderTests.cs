using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GymApp.Contracts;
using GymApp.Server.Push;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymApp.Server.Tests;

/// <summary>Test keys, made once per run: a P-256 key like an APNs .p8 file, and a service account JSON with an RSA key.</summary>
public static class PushTestKeys
{
    public static readonly ECDsa Apns = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public static readonly string ApnsPem = Apns.ExportPkcs8PrivateKeyPem();

    public static readonly string ServiceAccountJson = JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["type"] = "service_account",
        ["project_id"] = "gainstreak-test",
        ["private_key_id"] = "abc123",
        ["private_key"] = RSA.Create(2048).ExportPkcs8PrivateKeyPem(),
        ["client_email"] = "push@gainstreak-test.iam.gserviceaccount.com",
        ["client_id"] = "1",
        ["token_uri"] = "https://oauth2.googleapis.com/token"
    });

    public static Dictionary<string, string?> AllSettings() => new()
    {
        ["Push:Apns:KeyId"] = "ABC123DEFG",
        ["Push:Apns:TeamId"] = "TEAM123456",
        ["Push:Apns:PrivateKey"] = ApnsPem,
        ["Push:Fcm:ServiceAccountJson"] = ServiceAccountJson
    };
}

/// <summary>The APNs and FCM senders against a fake HTTP handler, and reading their settings (Phase 22b). No database.</summary>
public class PushSenderTests
{
    /// <summary>Records each request and answers from a queue (then 200).</summary>
    private sealed class FakeService : HttpMessageHandler, IHttpClientFactory
    {
        public readonly List<(HttpRequestMessage Request, string Body)> Requests = [];
        public readonly Queue<(HttpStatusCode Status, string Body)> Answers = new();

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, await request.Content!.ReadAsStringAsync(ct)));
            var (status, body) = Answers.Count > 0 ? Answers.Dequeue() : (HttpStatusCode.OK, "");
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private sealed class FakeTokens : IFcmAccessTokens
    {
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("access-token");
    }

    private static readonly ApnsSettings Apns = new("ABC123DEFG", "TEAM123456", PushTestKeys.ApnsPem, "com.gainstreak.app");
    private static readonly FcmSettings Fcm = new(PushTestKeys.ServiceAccountJson, "gainstreak-test");
    private static readonly PushMessage Request = new(PushKind.FriendRequest, 42, "alice");
    private readonly TestClock _clock = new() { Fixed = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero) };
    private readonly FakeService _service = new();

    private ApnsSender ApnsSender() => new(Apns, _service, _clock, NullLogger<ApnsSender>.Instance);
    private FcmSender FcmSender() => new(Fcm, new FakeTokens(), _service, NullLogger<FcmSender>.Instance);

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    // --- APNs ---

    [Fact] public void ApnsToken_IsAnES256Jwt_SignedWithTheKey()
    {
        var jwt = Push.ApnsSender.CreateToken(Apns, _clock.Fixed!.Value);
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        var header = Json(Encoding.UTF8.GetString(FromBase64Url(parts[0])));
        Assert.Equal(("ES256", "ABC123DEFG"), (header.GetProperty("alg").GetString(), header.GetProperty("kid").GetString()));
        var claims = Json(Encoding.UTF8.GetString(FromBase64Url(parts[1])));
        Assert.Equal("TEAM123456", claims.GetProperty("iss").GetString());
        Assert.Equal(_clock.Fixed.Value.ToUnixTimeSeconds(), claims.GetProperty("iat").GetInt64());
        Assert.True(PushTestKeys.Apns.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), FromBase64Url(parts[2]),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact] public async Task Apns_PostsOverHttp2_ToTheRightHost_WithTheHeadersAndBody()
    {
        var sender = ApnsSender();
        Assert.Equal(PushSendResult.Sent, await sender.SendAsync(new PushTarget(PushPlatform.Apns, "abcd1234", false), Request, default));
        Assert.Equal(PushSendResult.Sent, await sender.SendAsync(new PushTarget(PushPlatform.Apns, "ffff0000", true), Request, default));

        var (production, body) = _service.Requests[0];
        Assert.Equal("https://api.push.apple.com/3/device/abcd1234", production.RequestUri!.ToString());
        Assert.Equal("https://api.sandbox.push.apple.com/3/device/ffff0000", _service.Requests[1].Request.RequestUri!.ToString());
        Assert.Equal(HttpVersion.Version20, production.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, production.VersionPolicy);
        Assert.Equal("bearer", production.Headers.Authorization!.Scheme);
        string Header(string name) => Assert.Single(production.Headers.GetValues(name));
        Assert.Equal("com.gainstreak.app", Header("apns-topic"));
        Assert.Equal("alert", Header("apns-push-type"));
        Assert.Equal("10", Header("apns-priority"));
        Assert.Equal((_clock.Fixed!.Value + TimeSpan.FromDays(1)).ToUnixTimeSeconds().ToString(), Header("apns-expiration"));
        Assert.Equal("friendRequest-42", Header("apns-collapse-id"));

        var json = Json(body);
        var aps = json.GetProperty("aps");
        Assert.Equal("Friend request", aps.GetProperty("alert").GetProperty("title").GetString());
        Assert.Equal("alice wants to be your friend.", aps.GetProperty("alert").GetProperty("body").GetString());
        Assert.Equal("default", aps.GetProperty("sound").GetString());
        Assert.Equal("friends", aps.GetProperty("thread-id").GetString());
        Assert.Equal("friendRequest", json.GetProperty("kind").GetString());
        Assert.Equal("42", json.GetProperty("sender").GetString());
    }

    [Fact] public async Task Apns_ReusesItsToken_For40Minutes()
    {
        var sender = ApnsSender();
        var target = new PushTarget(PushPlatform.Apns, "abcd", false);
        await sender.SendAsync(target, Request, default);
        _clock.Fixed += TimeSpan.FromMinutes(39);
        await sender.SendAsync(target, Request, default);
        _clock.Fixed += TimeSpan.FromMinutes(1);
        await sender.SendAsync(target, Request, default);

        var auth = _service.Requests.Select(r => r.Request.Headers.Authorization!.Parameter).ToList();
        Assert.Equal(auth[0], auth[1]);
        Assert.NotEqual(auth[1], auth[2]);
    }

    [Fact] public async Task Apns_ExpiredProviderToken_MakesANewOne_AndTriesOnce()
    {
        _service.Answers.Enqueue((HttpStatusCode.Forbidden, """{"reason":"ExpiredProviderToken"}"""));
        Assert.Equal(PushSendResult.Sent, await ApnsSender().SendAsync(new PushTarget(PushPlatform.Apns, "abcd", false), Request, default));
        Assert.Equal(2, _service.Requests.Count);
        Assert.NotEqual(_service.Requests[0].Request.Headers.Authorization!.Parameter, _service.Requests[1].Request.Headers.Authorization!.Parameter);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, """{"reason":"Unregistered"}""", PushSendResult.DeadToken)]
    [InlineData(HttpStatusCode.BadRequest, """{"reason":"BadDeviceToken"}""", PushSendResult.DeadToken)]
    [InlineData(HttpStatusCode.BadRequest, """{"reason":"DeviceTokenNotForTopic"}""", PushSendResult.DeadToken)]
    [InlineData(HttpStatusCode.BadRequest, """{"reason":"PayloadTooLarge"}""", PushSendResult.Failed)]
    [InlineData(HttpStatusCode.Forbidden, """{"reason":"InvalidProviderToken"}""", PushSendResult.Failed)]
    [InlineData(HttpStatusCode.TooManyRequests, """{"reason":"TooManyRequests"}""", PushSendResult.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, "not json", PushSendResult.Failed)]
    public async Task Apns_Refusals(HttpStatusCode status, string body, PushSendResult expected)
    {
        _service.Answers.Enqueue((status, body));
        Assert.Equal(expected, await ApnsSender().SendAsync(new PushTarget(PushPlatform.Apns, "abcd", false), Request, default));
        Assert.Single(_service.Requests);
    }

    // --- FCM ---

    [Fact] public async Task Fcm_PostsTheV1Message_ToTheProject()
    {
        var accepted = new PushMessage(PushKind.FriendAccepted, 7, "bobby");
        Assert.Equal(PushSendResult.Sent, await FcmSender().SendAsync(new PushTarget(PushPlatform.Fcm, "fcm:token", false), accepted, default));

        var (request, body) = Assert.Single(_service.Requests);
        Assert.Equal("https://fcm.googleapis.com/v1/projects/gainstreak-test/messages:send", request.RequestUri!.ToString());
        Assert.Equal(("Bearer", "access-token"), (request.Headers.Authorization!.Scheme, request.Headers.Authorization.Parameter));
        var message = Json(body).GetProperty("message");
        Assert.Equal("fcm:token", message.GetProperty("token").GetString());
        Assert.Equal("Request accepted", message.GetProperty("notification").GetProperty("title").GetString());
        Assert.Equal("bobby accepted your friend request.", message.GetProperty("notification").GetProperty("body").GetString());
        Assert.Equal("friendAccepted", message.GetProperty("data").GetProperty("kind").GetString());
        Assert.Equal("7", message.GetProperty("data").GetProperty("sender").GetString());
        var android = message.GetProperty("android");
        Assert.Equal("86400s", android.GetProperty("ttl").GetString());
        Assert.Equal("high", android.GetProperty("priority").GetString());
        Assert.Equal("friendAccepted-7", android.GetProperty("collapse_key").GetString());
        Assert.Equal("friends", android.GetProperty("notification").GetProperty("channel_id").GetString());
        Assert.Equal("friendAccepted-7", android.GetProperty("notification").GetProperty("tag").GetString());
        Assert.Equal("ic_notification", android.GetProperty("notification").GetProperty("icon").GetString());
    }

    /// <summary>FCM answers 400 "Invalid data payload key: from" to a reserved key (22e, against the real service).</summary>
    [Fact] public void TheDataKeys_AvoidFcmsReservedOnes()
    {
        foreach (var kind in Enum.GetValues<PushKind>())
            foreach (var key in new PushMessage(kind, 1, "a").Data.Keys)
            {
                Assert.DoesNotContain(key, PushPayload.FcmReservedKeys);
                Assert.False(key.StartsWith("google", StringComparison.OrdinalIgnoreCase) || key.StartsWith("gcm", StringComparison.OrdinalIgnoreCase), key);
            }
    }

    private static string FcmError(int code, string status, string message, string? errorCode = null) =>
        JsonSerializer.Serialize(new
        {
            error = new
            {
                code, message, status,
                details = errorCode is null ? Array.Empty<object>()
                    : [new Dictionary<string, string> { ["@type"] = "type.googleapis.com/google.firebase.fcm.v1.FcmError", ["errorCode"] = errorCode }]
            }
        });

    public static TheoryData<HttpStatusCode, string, PushSendResult> FcmRefusals => new()
    {
        { HttpStatusCode.NotFound, FcmError(404, "NOT_FOUND", "Requested entity was not found.", "UNREGISTERED"), PushSendResult.DeadToken },
        { HttpStatusCode.Forbidden, FcmError(403, "PERMISSION_DENIED", "SenderId mismatch", "SENDER_ID_MISMATCH"), PushSendResult.DeadToken },
        { HttpStatusCode.BadRequest, FcmError(400, "INVALID_ARGUMENT", "The registration token is not a valid FCM registration token", "INVALID_ARGUMENT"), PushSendResult.DeadToken },
        { HttpStatusCode.BadRequest, FcmError(400, "INVALID_ARGUMENT", "Invalid value at 'message.android.ttl'", "INVALID_ARGUMENT"), PushSendResult.Failed },
        { HttpStatusCode.Unauthorized, FcmError(401, "UNAUTHENTICATED", "Request had invalid authentication credentials."), PushSendResult.Failed },
        { HttpStatusCode.TooManyRequests, FcmError(429, "RESOURCE_EXHAUSTED", "Quota exceeded", "QUOTA_EXCEEDED"), PushSendResult.Failed },
        { HttpStatusCode.ServiceUnavailable, "<html>down</html>", PushSendResult.Failed },
    };

    [Theory, MemberData(nameof(FcmRefusals))]
    public async Task Fcm_Refusals(HttpStatusCode status, string body, PushSendResult expected)
    {
        _service.Answers.Enqueue((status, body));
        Assert.Equal(expected, await FcmSender().SendAsync(new PushTarget(PushPlatform.Fcm, "t", false), Request, default));
    }

    /// <summary>Only that Google's library accepts the file: getting a real token needs Google.</summary>
    [Fact] public void GoogleTokens_ReadTheServiceAccount() => Assert.NotNull(new GoogleFcmAccessTokens(Fcm));

    // --- Settings ---

    private static PushSettings Read(Dictionary<string, string?> values) =>
        PushSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact] public void Settings_NoneSet_TurnsNothingOn()
    {
        Assert.Equal(new PushSettings(null, null), Read([]));
        Assert.Equal(new PushSettings(null, null), Read(new() { ["Push:Apns:KeyId"] = " ", ["Push:Fcm:ServiceAccountJson"] = "" }));
    }

    [Fact] public void Settings_AllSet()
    {
        var s = Read(PushTestKeys.AllSettings());
        Assert.Equal(new ApnsSettings("ABC123DEFG", "TEAM123456", PushTestKeys.ApnsPem.Trim(), "com.gainstreak.app"), s.Apns);
        Assert.Equal("gainstreak-test", s.Fcm!.ProjectId);

        var other = PushTestKeys.AllSettings();
        other["Push:Apns:BundleId"] = "com.example.other";
        Assert.Equal("com.example.other", Read(other).Apns!.BundleId);
    }

    public static TheoryData<string, string?, string> BadSettings => new()
    {
        { "Push:Apns:KeyId", null, "Set all of Push__Apns__KeyId" },
        { "Push:Apns:PrivateKey", null, "Set all of Push__Apns__KeyId" },
        { "Push:Apns:KeyId", "abc", "Push__Apns__KeyId" },
        { "Push:Apns:TeamId", "TEAM", "Push__Apns__TeamId" },
        { "Push:Apns:PrivateKey", "not a key", "Push__Apns__PrivateKey" },
        { "Push:Apns:PrivateKey", PushTestKeys.Apns.ExportSubjectPublicKeyInfoPem(), "Push__Apns__PrivateKey" },
        { "Push:Apns:PrivateKey", ECDsa.Create(ECCurve.NamedCurves.nistP384).ExportPkcs8PrivateKeyPem(), "Push__Apns__PrivateKey" },
        { "Push:Fcm:ServiceAccountJson", "{not json", "Push__Fcm__ServiceAccountJson" },
        { "Push:Fcm:ServiceAccountJson", """{"type":"authorized_user","project_id":"x"}""", "Push__Fcm__ServiceAccountJson" },
        { "Push:Fcm:ServiceAccountJson", PushTestKeys.ServiceAccountJson.Replace("PRIVATE KEY-----\\n", "PRIVATE KEY-----\\nbroken"), "Push__Fcm__ServiceAccountJson" },
    };

    [Theory, MemberData(nameof(BadSettings))]
    public void Settings_Malformed_StopStartup_WithoutQuotingTheValue(string key, string? value, string expected)
    {
        var values = PushTestKeys.AllSettings();
        values[key] = value;
        var e = Assert.Throws<InvalidOperationException>(() => Read(values));
        Assert.Contains(expected, e.Message);
        Assert.DoesNotContain("PRIVATE KEY", e.Message);
        if (value is { Length: > 8 }) Assert.DoesNotContain(value, e.Message);
    }
}
