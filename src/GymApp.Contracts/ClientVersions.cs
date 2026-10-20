namespace GymApp.Contracts;

/// <summary>
/// The app tells the server its version on every request, so the server can turn away builds too old to talk to it
/// (426 with a "please update" message) instead of failing in stranger ways. Versions are "major.minor.patch" as in
/// Directory.Build.props; anything after a '-' or '+' (a pre-release or commit suffix) is ignored.
/// </summary>
public static class ClientVersions
{
    public const string Header = "X-App-Version";

    /// <summary>The ProblemDetails extension on a 426 naming the oldest version the server accepts.</summary>
    public const string MinVersionField = "minVersion";

    /// <summary>"0.15.0", "0.15.0-beta", "0.15.0+abc123" → 0.15.0; null if it isn't a version.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var core = text.Trim().Split('-', '+')[0];
        if (!Version.TryParse(core, out var v)) return null;
        // "0.15" and "0.15.0" are the same release
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }
}
