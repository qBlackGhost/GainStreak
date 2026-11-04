using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GymApp.Services;

namespace GymApp.App;

/// <summary>Local notifications for the reminders (16g), from the head: <c>UNUserNotificationCenter</c> on iOS,
/// <c>AlarmManager</c> with a broadcast receiver on Android. The desktop has none.</summary>
public interface INotificationScheduler
{
    /// <summary>False on the desktop: Profile hides Reminders.</summary>
    bool IsSupported { get; }

    /// <summary>Asks the system for permission to notify, when the user turns Reminders on. True if allowed (already, or
    /// now).</summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>Schedules exactly <paramref name="reminders"/>, cancelling the reminders scheduled before. Never touches
    /// the workout nudge.</summary>
    void Replace(IReadOnlyList<PlannedReminder> reminders);

    /// <summary>"Continue your workout?" at <paramref name="at"/> (Phase 18), replacing an earlier nudge. Its own
    /// notification id, so it and the reminders never cancel each other. Tapping it opens Home.</summary>
    void ScheduleWorkoutNudge(DateTimeOffset at, string title, string body);

    /// <summary>Cancels the nudge, pending or already shown.</summary>
    void CancelWorkoutNudge();
}

/// <summary>The desktop's (and the tests'): no notifications.</summary>
public sealed class NoNotificationScheduler : INotificationScheduler
{
    public static readonly NoNotificationScheduler Instance = new();

    public bool IsSupported => false;
    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);
    public void Replace(IReadOnlyList<PlannedReminder> reminders) { }
    public void ScheduleWorkoutNudge(DateTimeOffset at, string title, string body) { }
    public void CancelWorkoutNudge() { }
}
