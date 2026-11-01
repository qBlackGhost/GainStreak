using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymApp.Contracts;

namespace GymApp.Client;

/// <summary>
/// The social server's API, one method per endpoint. Never throws for network trouble: every call comes back as an
/// <see cref="ApiResult"/> that is Ok, Refused (a 4xx with the server's message) or Offline (no connection, DNS,
/// timeout, 5xx). Only cancelling <c>ct</c> throws. Sends the token from <see cref="ITokenStore"/> when there is one,
/// but never changes it: signing in and out is the caller's job.
/// </summary>
public sealed class SocialApiClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly ITokenStore _tokens;
    private readonly TimeSpan _timeout;
    private readonly string? _appVersion;
    private int _offline;

    /// <param name="http">Its <see cref="HttpClient.BaseAddress"/> is the server (see <see cref="ServerAddress"/>).</param>
    /// <param name="timeout">How long a call may take before it counts as offline; <see cref="DefaultTimeout"/> if null.</param>
    /// <param name="appVersion">Sent as <see cref="ClientVersions.Header"/> on every call, so the server can turn away
    /// builds that are too old. Null sends none (a server with a minimum version then refuses every call).</param>
    public SocialApiClient(HttpClient http, ITokenStore tokens, TimeSpan? timeout = null, string? appVersion = null)
    {
        _http = http;
        _tokens = tokens;
        _timeout = timeout ?? DefaultTimeout;
        _appVersion = appVersion;
    }

    /// <summary>A signed-in call got 401: the token expired or was revoked. Not raised by login (a wrong password)
    /// or logout (the session is already gone, which is what logging out wanted).</summary>
    public event EventHandler? SessionExpired;

    /// <summary>A call reached the server after the previous one was offline.</summary>
    public event EventHandler? BackOnline;

    /// <summary>The server refused this build as too old (426). Carries the server's message to show.</summary>
    public event EventHandler<string>? UpdateRequired;

    // --- Accounts ---

    public Task<ApiResult<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default) =>
        SendAsync<AuthResponse>(HttpMethod.Post, "v1/auth/register", request, ct);

    public Task<ApiResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        SendAsync<AuthResponse>(HttpMethod.Post, "v1/auth/login", request, ct, expiresSession: false);

    public Task<ApiResult> LogoutAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "v1/auth/logout", null, ct, expiresSession: false);

    /// <summary>Always succeeds for a well-formed request: see <see cref="PasswordResetRequest"/>.</summary>
    public Task<ApiResult> RequestPasswordResetAsync(PasswordResetRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "v1/auth/password-reset", request, ct, expiresSession: false);

    public Task<ApiResult> ResetPasswordAsync(PasswordResetConfirmRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "v1/auth/password-reset/confirm", request, ct, expiresSession: false);

    public Task<ApiResult> SendConfirmCodeAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "v1/me/email/code", null, ct);

    public Task<ApiResult> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "v1/me/email/confirm", request, ct);

    public Task<ApiResult<MeResponse>> GetMeAsync(CancellationToken ct = default) =>
        SendAsync<MeResponse>(HttpMethod.Get, "v1/me", null, ct);

    public Task<ApiResult> ChangeEmailAsync(ChangeEmailRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, "v1/me/email", request, ct);

    public Task<ApiResult> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, "v1/me/password", request, ct);

    public Task<ApiResult> DeleteAccountAsync(DeleteAccountRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, "v1/me", request, ct);

    // --- Friends ---

    public Task<ApiResult<List<FoundUserResponse>>> SearchAsync(string query, CancellationToken ct = default) =>
        SendAsync<List<FoundUserResponse>>(HttpMethod.Get, $"v1/users/search?q={Uri.EscapeDataString(query)}", null, ct);

    public Task<ApiResult<List<FriendResponse>>> GetFriendsAsync(CancellationToken ct = default) =>
        SendAsync<List<FriendResponse>>(HttpMethod.Get, "v1/friends", null, ct);

    public Task<ApiResult<FriendRequestsResponse>> GetRequestsAsync(CancellationToken ct = default) =>
        SendAsync<FriendRequestsResponse>(HttpMethod.Get, "v1/friends/requests", null, ct);

    public Task<ApiResult<CountResponse>> GetIncomingCountAsync(CancellationToken ct = default) =>
        SendAsync<CountResponse>(HttpMethod.Get, "v1/friends/requests/count", null, ct);

    public Task<ApiResult<SendRequestResponse>> SendRequestAsync(int userId, CancellationToken ct = default) =>
        SendAsync<SendRequestResponse>(HttpMethod.Post, $"v1/friends/{userId}/request", null, ct);

    public Task<ApiResult> AcceptAsync(int userId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"v1/friends/{userId}/accept", null, ct);

    public Task<ApiResult> DeclineAsync(int userId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"v1/friends/{userId}/decline", null, ct);

    /// <summary>Withdraws a request you sent.</summary>
    public Task<ApiResult> CancelRequestAsync(int userId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"v1/friends/{userId}/request", null, ct);

    public Task<ApiResult> RemoveFriendAsync(int userId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"v1/friends/{userId}", null, ct);

    // --- Blocks and reports ---

    public Task<ApiResult<List<BlockedUserResponse>>> GetBlockedAsync(CancellationToken ct = default) =>
        SendAsync<List<BlockedUserResponse>>(HttpMethod.Get, "v1/blocks", null, ct);

    public Task<ApiResult> BlockAsync(int userId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"v1/blocks/{userId}", null, ct);

    public Task<ApiResult> UnblockAsync(int userId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"v1/blocks/{userId}", null, ct);

    public Task<ApiResult> ReportUserAsync(int userId, UserReportRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"v1/users/{userId}/report", request, ct);

    // --- Stats ---

    public Task<ApiResult> UploadStatsAsync(StatsUploadRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, "v1/me/stats", request, ct);

    /// <param name="today">The phone's own date: weeks are counted from it.</param>
    public Task<ApiResult<LeaderboardResponse>> GetLeaderboardAsync(LeaderboardPeriod period, DateOnly today, CancellationToken ct = default) =>
        SendAsync<LeaderboardResponse>(HttpMethod.Get,
            $"v1/leaderboard?period={(period == LeaderboardPeriod.ThisWeek ? "thisWeek" : "allTime")}&today={Date(today)}", null, ct);

    /// <summary>How many users train with each split, over every account.</summary>
    public Task<ApiResult<SplitUsageResponse>> GetSplitUsageAsync(CancellationToken ct = default) =>
        SendAsync<SplitUsageResponse>(HttpMethod.Get, "v1/splits/usage", null, ct);

    // --- Check-ins ---

    public Task<ApiResult> SetAvatarAsync(string? avatar, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, "v1/me/avatar", new AvatarRequest(avatar), ct);

    public Task<ApiResult> SetSharingAsync(bool share, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, "v1/me/sharing", new SharingRequest(share), ct);

    public Task<ApiResult> PutCheckInAsync(DateOnly date, CheckInRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"v1/me/checkins/{Date(date)}", request, ct);

    public Task<ApiResult> DeleteCheckInAsync(DateOnly date, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"v1/me/checkins/{Date(date)}", null, ct);

    /// <summary>Replaces every check-in the server holds for you; used for resyncs.</summary>
    public Task<ApiResult> ReplaceCheckInsAsync(CheckInsUploadRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, "v1/me/checkins", request, ct);

    public Task<ApiResult<List<FriendCheckInResponse>>> GetFriendCheckInsAsync(DateOnly since, CancellationToken ct = default) =>
        SendAsync<List<FriendCheckInResponse>>(HttpMethod.Get, $"v1/friends/checkins?since={Date(since)}", null, ct);

    // --- Feedback ---

    /// <summary>A problem report or an idea from the Feedback popup; scrub the report first (<see cref="FeedbackRules.Scrub"/>).</summary>
    public Task<ApiResult> SendFeedbackAsync(FeedbackRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "v1/feedback", request, ct);

    // --- Shares ---

    /// <summary>Sends a split or a workout to friends (server ids).</summary>
    public Task<ApiResult<SendShareResponse>> SendShareAsync(ShareRequest request, CancellationToken ct = default) =>
        SendAsync<SendShareResponse>(HttpMethod.Post, "v1/shares", request, ct);

    /// <summary>Splits and workouts friends sent you, newest first.</summary>
    public Task<ApiResult<List<ShareResponse>>> GetSharesAsync(CancellationToken ct = default) =>
        SendAsync<List<ShareResponse>>(HttpMethod.Get, "v1/shares", null, ct);

    /// <summary>Removes a share sent to you, once added or dismissed.</summary>
    public Task<ApiResult> RemoveShareAsync(long id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"v1/shares/{id}", null, ct);

    // --- Plumbing ---

    private static string Date(DateOnly date) => date.ToString(ApiFormat.Date, CultureInfo.InvariantCulture);

    private async Task<ApiResult> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct, bool expiresSession = true)
    {
        var result = await SendCoreAsync<object>(method, path, body, readValue: false, expiresSession, ct);
        return result.Outcome switch
        {
            ApiOutcome.Ok => ApiResult.Ok(result.Status!.Value),
            ApiOutcome.Refused => ApiResult.Refused(result.Status!.Value, result.Detail, result.Field),
            _ => ApiResult.Offline()
        };
    }

    private Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct, bool expiresSession = true) =>
        SendCoreAsync<T>(method, path, body, readValue: true, expiresSession, ct);

    private async Task<ApiResult<T>> SendCoreAsync<T>(HttpMethod method, string path, object? body, bool readValue,
        bool expiresSession, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: ApiFormat.Json);
        if (_tokens.Get() is { } token) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (_appVersion is not null) request.Headers.TryAddWithoutValidation(ClientVersions.Header, _appVersion);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        ApiResult<T> result;
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            if (status >= 500)
                result = ApiResult<T>.Offline();
            else if (response.IsSuccessStatusCode)
                result = !readValue
                    ? ApiResult<T>.Ok(default!, status)
                    : await response.Content.ReadFromJsonAsync<T>(ApiFormat.Json, timeout.Token) is { } value
                        ? ApiResult<T>.Ok(value, status)
                        : ApiResult<T>.Offline();
            else
            {
                var (detail, field) = await ReadProblemAsync(response, timeout.Token);
                result = ApiResult<T>.Refused(status, detail, field);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            result = ApiResult<T>.Offline();   // our timeout, not the caller cancelling
        }
        catch (HttpRequestException)
        {
            result = ApiResult<T>.Offline();   // no connection, DNS, connection reset
        }
        catch (JsonException)
        {
            result = ApiResult<T>.Offline();   // a 200 that isn't the API, e.g. a captive portal's page
        }

        if (result.IsOffline)
            Interlocked.Exchange(ref _offline, 1);
        else if (Interlocked.Exchange(ref _offline, 0) == 1)
            BackOnline?.Invoke(this, EventArgs.Empty);
        if (result.Status == 401 && expiresSession)
            SessionExpired?.Invoke(this, EventArgs.Empty);
        if (result.IsUpdateRequired)
            UpdateRequired?.Invoke(this, result.Error!);
        return result;
    }

    /// <summary>The ProblemDetails' <c>detail</c> and <c>field</c>; nulls if the body is empty or isn't one.</summary>
    private static async Task<(string? Detail, string? Field)> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(text)) return (null, null);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, null);
            return (StringProperty(doc.RootElement, "detail"), StringProperty(doc.RootElement, "field"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? StringProperty(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
