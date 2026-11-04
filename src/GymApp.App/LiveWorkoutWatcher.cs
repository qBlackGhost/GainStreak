using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App;

/// <summary>
/// Phase 18b: watches an open live workout while the app comes and goes. Going to the background with a set logged
/// schedules "Continue your workout?" for 12 minutes later; coming back cancels it and, after 12 minutes or more away,
/// asks Home for the return popup. A session past 4 hours or midnight is ended on coming back and once a minute while
/// the app is open (<see cref="TickAsync"/>), and Home shows the one-time note. Everything lives in the database, so it
/// all works after the phone kills the app. Nothing happens where there's no live workout (the desktop).
/// </summary>
public sealed class LiveWorkoutWatcher(LiveWorkoutService live, INotificationScheduler scheduler, SessionService session,
    IPlatform platform, ErrorLog log)
{
    public const string NudgeTitle = "Continue your workout?";

    /// <summary>How long away before the nudge and the popup. Debug builds read GYMAPP_NUDGE_MINUTES (e.g. 1) to check
    /// it without waiting.</summary>
    public TimeSpan NudgeAfter { get; set; } = DebugNudgeAfter() ?? LiveWorkoutRules.NudgeAfter;

    /// <summary>Home should show "Continue your workout?" (away 12 minutes or more). Cleared by Continue or Finish.</summary>
    public bool ReturnPending { get; private set; }

    private Action? _listener;

    /// <summary>The Home on screen hears about the return popup and a session that stopped by itself. One at a time:
    /// a new Home replaces the last one.</summary>
    public void Listen(Action changed) => _listener = changed;

    private int? UserId => platform.HasLiveWorkout ? session.CurrentUser?.Id : null;

    public async Task WentToBackgroundAsync()
    {
        if (UserId is not { } userId) return;
        try
        {
            if (await live.GetOpenAsync(userId) is not { } open) return;
            await live.TouchAsync(userId);   // away from now
            if (!open.Exercises.Any(e => e.TodaySets.Count > 0)) return;
            var next = LiveWorkoutRules.Current(open.Plan, open.CurrentExerciseId, open.SkippedExerciseIds) is { } c
                ? open.Exercises.First(e => e.ExerciseId == c.ExerciseId).Name
                : null;
            scheduler.ScheduleWorkoutNudge(DateTimeOffset.Now + NudgeAfter, NudgeTitle,
                next is null ? "Finish it before you go." : $"{next} is next.");
        }
        catch (Exception e) { log.Write(e, "Live workout (background)"); }
    }

    /// <summary>Coming back to the front, and at a cold start.</summary>
    public async Task CameToForegroundAsync()
    {
        scheduler.CancelWorkoutNudge();
        if (UserId is not { } userId) return;
        try
        {
            if (await live.StopIfDueAsync(userId)) { ReturnPending = false; _listener?.Invoke(); return; }
            if (await live.AwayForAsync(userId) is { } away && away >= NudgeAfter)
            {
                ReturnPending = true;
                _listener?.Invoke();
            }
        }
        catch (Exception e) { log.Write(e, "Live workout (foreground)"); }
    }

    /// <summary>Once a minute while the app is open: the 4-hour and midnight stop.</summary>
    public async Task TickAsync()
    {
        if (UserId is not { } userId) return;
        try
        {
            if (await live.StopIfDueAsync(userId)) { ReturnPending = false; _listener?.Invoke(); }
        }
        catch (Exception e) { log.Write(e, "Live workout (timer)"); }
    }

    /// <summary>"Continue" in the popup: the 12 minutes start again.</summary>
    public async Task ContinueAsync()
    {
        ReturnPending = false;
        if (UserId is { } userId) await live.TouchAsync(userId);
    }

    /// <summary>The popup was answered another way (Finish workout early), or the workout ended.</summary>
    public void ClearReturn() => ReturnPending = false;

    private static TimeSpan? DebugNudgeAfter()
    {
#if DEBUG
        if (double.TryParse(Environment.GetEnvironmentVariable("GYMAPP_NUDGE_MINUTES"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
            return TimeSpan.FromMinutes(minutes);
#endif
        return null;
    }
}
