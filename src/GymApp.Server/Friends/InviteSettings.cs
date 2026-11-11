using System.Text.RegularExpressions;

namespace GymApp.Server.Friends;

/// <summary>
/// Settings for friend invite links (Phase 21), all optional, from the <c>Invites</c> section
/// (<c>Invites__BaseUrl</c> and so on in production):
/// <list type="bullet">
/// <item><c>BaseUrl</c>: where links point, e.g. <c>https://gainstreak.app</c>. Unset: the request's own scheme and host.</item>
/// <item><c>AppStoreUrl</c> / <c>PlayStoreUrl</c>: where the landing page sends phones without the app (TestFlight and
/// the Play testing link during the beta). Play defaults to the app's store page; no App Store link until it's set.</item>
/// <item><c>AppleTeamId</c>: turns on <c>/.well-known/apple-app-site-association</c>, so iOS opens links in the app.</item>
/// <item><c>AndroidCertFingerprints</c>: SHA-256 fingerprints of the signing keys (Play app signing and upload),
/// separated by ';'. Turns on <c>/.well-known/assetlinks.json</c>, so Android opens links in the app.</item>
/// </list>
/// A value that's set but malformed is a startup error, like the other settings.
/// </summary>
public sealed partial record InviteSettings(
    Uri? BaseUrl, string? AppStoreUrl, string PlayStoreUrl, string? AppleTeamId, IReadOnlyList<string> AndroidCertFingerprints)
{
    public const string SectionName = "Invites";
    public const string AppId = "com.gainstreak.app";
    public const string DefaultPlayStoreUrl = $"https://play.google.com/store/apps/details?id={AppId}";

    public static InviteSettings Read(IConfiguration config)
    {
        var s = config.GetSection(SectionName);

        Uri? baseUrl = null;
        if (Value(s["BaseUrl"]) is { } b)
        {
            if (!Uri.TryCreate(b.TrimEnd('/'), UriKind.Absolute, out baseUrl) || baseUrl.Scheme != Uri.UriSchemeHttps || baseUrl.AbsolutePath != "/")
                throw new InvalidOperationException("Invites__BaseUrl must be an https address with no path, like https://gainstreak.app.");
        }

        var appStore = Https(s["AppStoreUrl"], "Invites__AppStoreUrl");
        var playStore = Https(s["PlayStoreUrl"], "Invites__PlayStoreUrl") ?? DefaultPlayStoreUrl;

        var teamId = Value(s["AppleTeamId"]);
        if (teamId is not null && !TeamIdPattern().IsMatch(teamId))
            throw new InvalidOperationException("Invites__AppleTeamId must be the 10-character Apple Team ID.");

        var fingerprints = (Value(s["AndroidCertFingerprints"]) ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.ToUpperInvariant())
            .ToList();
        if (fingerprints.Any(f => !FingerprintPattern().IsMatch(f)))
            throw new InvalidOperationException(
                "Invites__AndroidCertFingerprints must be SHA-256 fingerprints like AB:CD:…, 32 pairs each, separated by ';'.");

        return new InviteSettings(baseUrl, appStore, playStore, teamId, fingerprints);
    }

    /// <summary>The invite link for <paramref name="code"/>, on <see cref="BaseUrl"/> or the request's own host.</summary>
    public string LinkFor(string code, HttpRequest request)
    {
        var root = BaseUrl?.GetLeftPart(UriPartial.Authority) ?? $"{request.Scheme}://{request.Host}";
        return $"{root}{Social.FriendInvites.LinkPath}{code}";
    }

    private static string? Value(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static string? Https(string? v, string name)
    {
        if (Value(v) is not { } url) return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
            ? url
            : throw new InvalidOperationException($"{name} must be an https address.");
    }

    [GeneratedRegex("^[A-Z0-9]{10}$")]
    private static partial Regex TeamIdPattern();

    [GeneratedRegex("^[0-9A-F]{2}(:[0-9A-F]{2}){31}$")]
    private static partial Regex FingerprintPattern();
}
