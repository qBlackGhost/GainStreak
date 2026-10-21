using System.Linq;
using System.Reflection;

namespace GymApp.Desktop;

/// <summary>The release settings the app was published with (GymAppServerUrl and GymAppUpdateUrl in
/// Directory.Build.props), stored as assembly metadata. Null when not set, as in a normal Debug build.</summary>
public static class BuildSettings
{
    public static string? ServerUrl { get; } = Get("GymAppServerUrl");
    public static string? UpdateUrl { get; } = Get("GymAppUpdateUrl");

    private static string? Get(string key) =>
        typeof(BuildSettings).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } value ? value : null;
}
