using System;
using System.Threading.Tasks;
using GymApp.Core;

namespace GymApp.App;

/// <summary>
/// The phone's position for "verified at gym" (18c), from the head: <c>CLLocationManager</c> on iOS (when in use only),
/// <c>LocationManager</c> on Android (no Google Play Services). Read only while a live workout is open on screen, one
/// fix at a time; never in the background. The fix stays on the phone (<see cref="GymProximity"/>). The desktop has none.
/// </summary>
public interface ILocationProvider
{
    /// <summary>False on the desktop: workouts there are never verified.</summary>
    bool IsSupported { get; }

    /// <summary>The user allowed location while the app is in use.</summary>
    bool HasPermission { get; }

    /// <summary>The permission includes precise location. Without it (approximate on Android, Precise Location off on
    /// iOS) fixes are too vague to verify a workout, so the card says so instead of reading them.</summary>
    bool IsPrecise { get; }

    /// <summary>The system's prompt shows no explanation from the app (Android), so Home shows a short note first.
    /// iOS shows the app's own sentence in its prompt.</summary>
    bool NeedsNoteBeforePrompt { get; }

    /// <summary>Asks for location while in use, at the first Start workout. True if allowed (already, or now).</summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>One fix, or null when there's none within <paramref name="timeout"/> (or no permission).</summary>
    Task<LocationFix?> GetFixAsync(TimeSpan timeout);

    /// <summary>The app's page in the phone's Settings, for "Location is off".</summary>
    void OpenSettings();
}

/// <summary>The desktop's: no location.</summary>
public sealed class NoLocationProvider : ILocationProvider
{
    public static readonly NoLocationProvider Instance = new();

    public bool IsSupported => false;
    public bool HasPermission => false;
    public bool IsPrecise => false;
    public bool NeedsNoteBeforePrompt => false;
    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);
    public Task<LocationFix?> GetFixAsync(TimeSpan timeout) => Task.FromResult<LocationFix?>(null);
    public void OpenSettings() { }
}
