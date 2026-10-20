using System.Reflection;

namespace GymApp.Desktop;

public static class AppVersion
{
    /// <summary>The release version, e.g. "0.14.1", from <c>Version</c> in Directory.Build.props. The informational
    /// version can carry a "+commit" suffix from the SDK, which is cut off.</summary>
    public static string Current { get; } =
        (typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(3)
         ?? "?").Split('+')[0];
}
