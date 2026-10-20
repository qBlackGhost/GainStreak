using System.Net;
using System.Text;
using GymApp.Client;
using GymApp.Contracts;

namespace GymApp.Tests;

public class SocialApiClientTests
{
    private static readonly Uri Base = new("http://social.test/");

    private static SocialApiClient Api(FakeHttpHandler h, string? token = null, TimeSpan? timeout = null) =>
        new(new HttpClient(h) { BaseAddress = Base }, new MemoryTokenStore(token), timeout);

    private static FakeHttpHandler Status(HttpStatusCode status, string body = "", string mediaType = "application/problem+json") =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) }));

    private static string Problem(int status, string detail, string? field = null) =>
        $$"""{"type":"x","title":"t","status":{{status}},"detail":"{{detail}}"{{(field is null ? "" : $",\"field\":\"{field}\"")}}}""";

    // --- Ok ---

    [Fact] public async Task Ok_ReadsTheValue_WithTheApiFormat()
    {
        var h = FakeHttpHandler.Json("""{"id":7,"username":"alice","email":"a@example.com","shareGymCheckIns":true}""");
        var r = await Api(h, "tok").GetMeAsync();

        Assert.True(r.IsOk);
        Assert.Equal(new MeResponse(7, "alice", "a@example.com", true), r.Value);
        Assert.Null(r.Error);
        Assert.Equal("http://social.test/v1/me", Assert.Single(h.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact] public async Task NoContent_IsOk()
    {
        var r = await Api(Status(HttpStatusCode.NoContent), "tok").AcceptAsync(5);
        Assert.Equal((ApiOutcome.Ok, 204), (r.Outcome, r.Status));
    }

    [Fact] public async Task Query_IsEscaped_AndDatesAndPeriodsUseTheWireFormat()
    {
        var h = FakeHttpHandler.Json("[]");
        var api = Api(h, "tok");
        await api.SearchAsync("al ice&x");
        await api.GetFriendCheckInsAsync(new DateOnly(2026, 3, 4));
        h = FakeHttpHandler.Json("""{"period":"thisWeek","weekStart":"2026-03-02","weekEnd":"2026-03-08","rows":[]}""");
        var board = await Api(h, "tok").GetLeaderboardAsync(LeaderboardPeriod.ThisWeek, new DateOnly(2026, 3, 4));

        Assert.True(board.IsOk);
        Assert.Equal("http://social.test/v1/leaderboard?period=thisWeek&today=2026-03-04", Assert.Single(h.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact] public async Task Body_IsSentAsCamelCaseJson_WithEnumsAsStrings()
    {
        string? sent = null;
        var h = new FakeHttpHandler(async (req, ct) =>
        {
            sent = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        await Api(h, "tok").PutCheckInAsync(new DateOnly(2026, 3, 4),
            new CheckInRequest(OsmType.Way, 1, "Gym", OsmType.Relation, 2, "City", null, null));

        Assert.Equal("http://social.test/v1/me/checkins/2026-03-04", Assert.Single(h.Requests).RequestUri!.AbsoluteUri);
        Assert.Contains("\"gymOsmType\":\"way\"", sent);
        Assert.Contains("\"cityName\":\"City\"", sent);
    }

    // --- Refused ---

    [Fact] public async Task ProblemDetails_BecomeRefused_WithDetailAndField()
    {
        var r = await Api(Status(HttpStatusCode.Conflict, Problem(409, "That username is taken.", "username")))
            .RegisterAsync(new RegisterRequest("alice", "a@example.com", "pw", "phone"));

        Assert.Equal(ApiOutcome.Refused, r.Outcome);
        Assert.Equal((409, "That username is taken.", "username"), (r.Status, r.Detail, r.Field));
        Assert.Equal("That username is taken.", r.Error);
        Assert.Null(r.Value);
    }

    [Fact] public async Task RefusedWithoutProblemBody_GetsAFallbackMessage()
    {
        var notFound = await Api(Status(HttpStatusCode.NotFound), "tok").GetMeAsync();
        Assert.Equal((ApiOutcome.Refused, 404, null), (notFound.Outcome, notFound.Status, notFound.Detail));
        Assert.Equal(ApiResult.FallbackMessage, notFound.Error);

        var html = await Api(Status(HttpStatusCode.BadRequest, "<html>nope</html>", "text/html"), "tok").GetMeAsync();
        Assert.Equal(ApiResult.FallbackMessage, html.Error);

        var tooMany = await Api(Status(HttpStatusCode.TooManyRequests), "tok").SearchAsync("al");
        Assert.Equal(ApiResult.TooManyMessage, tooMany.Error);
    }

    // --- Offline ---

    [Fact] public async Task NoConnection_IsOffline()
    {
        var r = await Api(FakeHttpHandler.Throws(new HttpRequestException("Connection refused")), "tok").GetFriendsAsync();

        Assert.Equal(ApiOutcome.Offline, r.Outcome);
        Assert.Equal("You're offline. Try again when you're connected.", r.Error);
        Assert.Null(r.Status);
    }

    [Fact] public async Task Timeout_IsOffline()
    {
        var h = new FakeHttpHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var r = await Api(h, "tok", TimeSpan.FromMilliseconds(50)).GetFriendsAsync();
        Assert.True(r.IsOffline);
    }

    [Fact] public async Task CallerCancelling_Throws_InsteadOfOffline()
    {
        var h = new FakeHttpHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Api(h, "tok").GetFriendsAsync(cts.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task ServerErrors_AreOffline(HttpStatusCode status)
    {
        var r = await Api(Status(status, Problem((int)status, "boom")), "tok").SetSharingAsync(true);
        Assert.True(r.IsOffline);
        Assert.Equal(ApiResult.OfflineMessage, r.Error);
    }

    [Fact] public async Task OkThatIsNotTheApi_IsOffline()
    {
        // A captive portal answering 200 with its own page
        var r = await Api(Status(HttpStatusCode.OK, "<html>Sign in to the Wi-Fi</html>", "text/html"), "tok").GetMeAsync();
        Assert.True(r.IsOffline);
    }

    [Fact] public async Task BackOnline_IsRaised_WhenACallSucceedsAfterAnOfflineOne()
    {
        var offline = false;
        var h = new FakeHttpHandler((_, _) => offline
            ? throw new HttpRequestException("down")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var api = Api(h, "tok");
        var raised = 0;
        api.BackOnline += (_, _) => raised++;

        await api.AcceptAsync(1);   // ok before any offline call: nothing to report
        await api.AcceptAsync(1);
        Assert.Equal(0, raised);

        offline = true;
        await api.AcceptAsync(1);
        await api.AcceptAsync(1);
        offline = false;
        await api.AcceptAsync(1);
        await api.AcceptAsync(1);
        Assert.Equal(1, raised);
    }

    // --- Session ---

    [Fact] public async Task Unauthorized_RaisesSessionExpired()
    {
        var api = Api(Status(HttpStatusCode.Unauthorized), "old token");
        var raised = 0;
        api.SessionExpired += (_, _) => raised++;

        var r = await api.GetFriendsAsync();

        Assert.Equal((ApiOutcome.Refused, 401), (r.Outcome, r.Status));
        Assert.Equal(1, raised);
    }

    [Fact] public async Task UnauthorizedLoginOrLogout_DoesNotRaiseSessionExpired()
    {
        var api = Api(Status(HttpStatusCode.Unauthorized, Problem(401, "Wrong username or password.")), "old token");
        var raised = 0;
        api.SessionExpired += (_, _) => raised++;

        var login = await api.LoginAsync(new LoginRequest("alice", "wrong", "phone"));
        await api.LogoutAsync();

        Assert.Equal("Wrong username or password.", login.Error);
        Assert.Equal(0, raised);
    }

    [Fact] public async Task BearerHeader_IsSentOnlyWithAToken()
    {
        var h = Status(HttpStatusCode.NoContent);
        var tokens = new MemoryTokenStore();
        var api = new SocialApiClient(new HttpClient(h) { BaseAddress = Base }, tokens);

        await api.AcceptAsync(1);
        tokens.Set("abc");
        await api.AcceptAsync(1);
        tokens.Clear();
        await api.AcceptAsync(1);

        Assert.Equal([null, "Bearer abc", null], h.Requests.Select(r => r.Headers.Authorization?.ToString()));
    }

    // --- Version check ---

    [Fact] public async Task AppVersion_IsSentOnEveryCall_WhenGiven()
    {
        var h = Status(HttpStatusCode.NoContent);
        await new SocialApiClient(new HttpClient(h) { BaseAddress = Base }, new MemoryTokenStore(), appVersion: "0.15.0").AcceptAsync(1);
        await Api(h).AcceptAsync(1);

        Assert.Equal(["0.15.0"], h.Requests[0].Headers.GetValues(ClientVersions.Header));
        Assert.False(h.Requests[1].Headers.Contains(ClientVersions.Header));
    }

    [Fact] public async Task UpgradeRequired_RaisesUpdateRequired_WithTheServersMessage_AndNotSessionExpired()
    {
        var api = Api(Status(HttpStatusCode.UpgradeRequired, Problem(426, "Too old. Update the app.")), "tok");
        var messages = new List<string>();
        var expired = 0;
        api.UpdateRequired += (_, m) => messages.Add(m);
        api.SessionExpired += (_, _) => expired++;

        var r = await api.GetFriendsAsync();

        Assert.True(r.IsUpdateRequired);
        Assert.False(r.IsOffline);
        Assert.Equal(["Too old. Update the app."], messages);
        Assert.Equal(0, expired);
    }

    [Fact] public async Task UpgradeRequired_WithNoBody_HasItsOwnMessage()
    {
        var api = Api(Status(HttpStatusCode.UpgradeRequired));
        var r = await api.LoginAsync(new LoginRequest("alice", "pw", "phone"));
        Assert.Equal(ApiResult.UpdateRequiredMessage, r.Error);
    }

    [Theory]
    [InlineData("0.15.0", 0, 15, 0)]
    [InlineData("0.15", 0, 15, 0)]
    [InlineData(" 1.2.3-beta ", 1, 2, 3)]
    [InlineData("0.15.1+abc123", 0, 15, 1)]
    public void ClientVersions_Parse(string text, int major, int minor, int patch) =>
        Assert.Equal(new Version(major, minor, patch), ClientVersions.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1")]
    public void ClientVersions_Parse_NotAVersion_IsNull(string? text) => Assert.Null(ClientVersions.Parse(text));
}

public class TokenStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"gymapp-token-{Guid.NewGuid():N}");
    private string File_ => Path.Combine(_dir, "session.token");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact] public void FileStore_SurvivesANewInstance_AndClearDeletesTheFile()
    {
        new FileTokenStore(File_).Set("secret");
        Assert.Equal("secret", new FileTokenStore(File_).Get());

        var store = new FileTokenStore(File_);
        store.Clear();
        Assert.Null(store.Get());
        Assert.False(File.Exists(File_));
        Assert.Null(new FileTokenStore(File_).Get());
    }

    [Fact] public void FileStore_NoFile_IsNoToken()
    {
        Assert.Null(new FileTokenStore(File_).Get());
        new FileTokenStore(File_).Clear();   // clearing nothing is fine
    }

    [Fact] public void FileStore_IsReadableOnlyByTheUser()
    {
        if (OperatingSystem.IsWindows()) return;
        var store = new FileTokenStore(File_);
        store.Set("one");
        store.Set("two");   // replacing keeps the mode

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(File_));
        Assert.False(File.Exists(File_ + ".tmp"));
    }
}

public class ServerAddressTests
{
    [Fact] public void Debug_DefaultsToLocalhost() =>
        Assert.Equal("http://localhost:5080/", ServerAddress.Resolve(null, debugBuild: true).AbsoluteUri);

    [Fact] public void Setting_WinsInDebug_AndKeepsItsPath() =>
        Assert.Equal("http://192.168.1.5:5080/api/", ServerAddress.Resolve(" http://192.168.1.5:5080/api ", debugBuild: true).AbsoluteUri);

    [Fact] public void Release_NeedsAnHttpsSetting()
    {
        Assert.Equal("https://social.example.com/", ServerAddress.Resolve("https://social.example.com", debugBuild: false).AbsoluteUri);
        Assert.Throws<InvalidOperationException>(() => ServerAddress.Resolve(null, debugBuild: false));
        Assert.Throws<InvalidOperationException>(() => ServerAddress.Resolve("http://social.example.com", debugBuild: false));
    }

    [Theory]
    [InlineData("localhost:5080")]
    [InlineData("ftp://example.com")]
    [InlineData("not a url")]
    public void BadSetting_Throws(string value) =>
        Assert.Throws<InvalidOperationException>(() => ServerAddress.Resolve(value, debugBuild: true));
}
