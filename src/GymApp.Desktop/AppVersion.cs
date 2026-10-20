using System.Reflection;
using System.Runtime.InteropServices;

namespace GymApp.Desktop;

public static class AppVersion
{
    /// <summary>The release version, e.g. "0.14.1", from <c>Version</c> in Directory.Build.props. The informational
    /// version can carry a "+commit" suffix from the SDK, which is cut off.</summary>
    public static string Current { get; } =
        (typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(3)
         ?? "?").Split('+')[0];

    /// <summary>"GainStreak 0.15.0 on Darwin 25.1.0 (Arm64)": stamped on every errors.log entry and error report.</summary>
    public static string Description { get; } =
        $"GainStreak {Current} on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
}
