namespace GymApp.Core;

/// <summary>Where a tapped Home Screen widget or Control Center control opens the app (Phase 24).</summary>
public enum WidgetTarget
{
    /// <summary>Home: the streak.</summary>
    Home,
    /// <summary>Today's planned workout day (Splits on a rest day or with no split).</summary>
    Plan,
    /// <summary>Home with the points history open.</summary>
    Points,
    /// <summary>Profile's body data: weight and steps.</summary>
    Body,
    /// <summary>Home, starting today's workout when it can (the "Start workout" control).</summary>
    StartWorkout,
}

/// <summary>
/// The widgets' links (Phase 24): <c>gainstreak://open/&lt;target&gt;</c>, written by the Swift widget extension
/// (src/GymApp.iOS.LiveActivity/Widget/HomeWidgets.swift) and opened by the app. Keep the names in step with it.
/// </summary>
public static class WidgetLink
{
    public const string Scheme = "gainstreak";
    public const string Host = "open";

    private static readonly (string Path, WidgetTarget Target)[] Paths =
    [
        ("home", WidgetTarget.Home),
        ("plan", WidgetTarget.Plan),
        ("points", WidgetTarget.Points),
        ("body", WidgetTarget.Body),
        ("start", WidgetTarget.StartWorkout),
    ];

    /// <summary>The target of a widget link, or null for any other link (an invite link, an unknown target).</summary>
    public static WidgetTarget? TryParse(Uri? link)
    {
        if (link is not { IsAbsoluteUri: true } || !link.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase) ||
            !link.Host.Equals(Host, StringComparison.OrdinalIgnoreCase))
            return null;
        var path = link.AbsolutePath.Trim('/');
        foreach (var (name, target) in Paths)
            if (path.Equals(name, StringComparison.OrdinalIgnoreCase)) return target;
        return null;
    }

    public static Uri For(WidgetTarget target) =>
        new($"{Scheme}://{Host}/{Paths.First(p => p.Target == target).Path}");
}
