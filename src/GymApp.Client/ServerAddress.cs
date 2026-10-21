namespace GymApp.Client;

/// <summary>
/// The social server's base URL, decided in one place. Debug builds use <see cref="DebugDefault"/> (the server's
/// launch profile) unless <see cref="EnvironmentVariable"/> says otherwise. Release builds use the address they were
/// published with (GymAppServerUrl) unless the variable overrides it, and it must be https: a session token must
/// never travel in the clear.
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

    /// <summary>The address for this build: the environment's if set, else the debug default or, in a release
    /// build, <paramref name="builtIn"/> (the address the app was published with).</summary>
    public static Uri FromEnvironment(string? builtIn = null) =>
        Resolve(Environment.GetEnvironmentVariable(EnvironmentVariable), IsDebugBuild, builtIn);

    /// <summary>The rule itself: <paramref name="configured"/> if set, else the debug default in a debug build and
    /// <paramref name="builtIn"/> in a release one. Always ends in '/', so relative API paths keep any path prefix
    /// the server is hosted under.</summary>
    /// <exception cref="InvalidOperationException">No address, not an absolute http(s) URL, or not https in a release build.</exception>
    public static Uri Resolve(string? configured, bool debugBuild, string? builtIn = null)
    {
        var value = Trimmed(configured) ?? (debugBuild ? DebugDefault : Trimmed(builtIn));
        if (value is null)
            throw new InvalidOperationException(
                $"This build has no server address. Publish it with -p:GymAppServerUrl=https://... or set {EnvironmentVariable}.");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"{EnvironmentVariable} must be an http or https URL, e.g. {DebugDefault}.");
        if (!debugBuild && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"{EnvironmentVariable} must be an https address in a release build.");
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
