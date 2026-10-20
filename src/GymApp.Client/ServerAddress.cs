namespace GymApp.Client;

/// <summary>
/// The social server's base URL, decided in one place. Debug builds use <see cref="DebugDefault"/> (the server's
/// launch profile) unless <see cref="EnvironmentVariable"/> says otherwise. Release builds must set it, and to an
/// https address: a session token must never travel in the clear.
/// </summary>
public static class ServerAddress
{
    public const string EnvironmentVariable = "GYMAPP_SERVER_URL";
    public const string DebugDefault = "http://localhost:5080";

#if DEBUG
    private const bool IsDebugBuild = true;
#else
    private const bool IsDebugBuild = false;
#endif

    /// <summary>The address for this build, from the environment.</summary>
    public static Uri FromEnvironment() => Resolve(Environment.GetEnvironmentVariable(EnvironmentVariable), IsDebugBuild);

    /// <summary>The rule itself: <paramref name="configured"/> if set, else the debug default. Always ends in '/',
    /// so relative API paths keep any path prefix the server is hosted under.</summary>
    /// <exception cref="InvalidOperationException">The setting is missing, not an absolute http(s) URL, or not https in a release build.</exception>
    public static Uri Resolve(string? configured, bool debugBuild)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
        if (value is null)
        {
            if (!debugBuild) throw new InvalidOperationException($"Set {EnvironmentVariable} to the social server's https address.");
            value = DebugDefault;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"{EnvironmentVariable} must be an http or https URL, e.g. {DebugDefault}.");
        if (!debugBuild && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"{EnvironmentVariable} must be an https address in a release build.");
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}
