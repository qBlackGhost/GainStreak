using System;

namespace GymApp.Core;

/// <summary>Why a cardio timer isn't running: the user paused it, or a check found the phone away from the gym.</summary>
public enum CardioPause { User, LeftGym }

/// <summary>
/// A cardio session's clock (0.18.2): the seconds already counted, plus the run since <see cref="RunningSince"/> when
/// it runs. <see cref="LastAtGymAt"/> is the last check that found the phone at the workout's gym; null when the
/// session isn't checked (the workout wasn't verified at a gym).
/// </summary>
public record CardioClock(int CountedSeconds, DateTime? RunningSince, DateTime? LastAtGymAt, bool IsChecked);

/// <summary>
/// The cardio timer's rules (0.18.2). After a live workout the user can time cardio; the minutes go onto today's log
/// and earn <see cref="PointsCalculator.CardioPoints"/> as typed minutes did. When the workout was verified at a gym, the
/// phone's position is checked while the timer runs: time counts while it's there. A phone can't be checked while it's
/// locked, so time away from the app counts if the next check still finds it at the gym; found elsewhere, the timer
/// pauses at the last check that found it there.
/// </summary>
public static class CardioRules
{
    /// <summary>How often the open app checks the position while the timer runs.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    /// <summary>The seconds counted by <paramref name="now"/>.</summary>
    public static int Seconds(CardioClock clock, DateTime now) =>
        clock.CountedSeconds + (clock.RunningSince is { } since ? WholeSeconds(now - since) : 0);

    /// <summary>The seconds counted when a check finds the phone away: only up to the last check that found it at the
    /// gym. Unchecked, or never found there since it started running: nothing more.</summary>
    public static int SecondsWhenLeft(CardioClock clock) =>
        clock.CountedSeconds + (clock is { RunningSince: { } since, LastAtGymAt: { } last } ? WholeSeconds(last - since) : 0);

    /// <summary>The seconds counted when the session stops by itself at <paramref name="stopAt"/> (4 hours, or
    /// midnight): a checked one only up to its last check at the gym, since nothing says the user stayed.</summary>
    public static int SecondsAtStop(CardioClock clock, DateTime stopAt)
    {
        if (clock.RunningSince is not { } since) return clock.CountedSeconds;
        var until = clock.IsChecked && clock.LastAtGymAt is { } last && last < stopAt ? last : stopAt;
        return clock.CountedSeconds + WholeSeconds(until - since);
    }

    /// <summary>Whole minutes, as saved on the log; never more than a log accepts.</summary>
    public static int Minutes(int seconds) => Math.Min(PointsCalculator.MaxCardioMinutes, Math.Max(0, seconds) / 60);

    /// <summary>The minute the next point comes at, or null once cardio earns no more.</summary>
    public static int? NextPointAt(int minutes)
    {
        var per = PointsCalculator.CardioMinutesPerPoint;
        if (PointsCalculator.CardioPoints(minutes) >= PointsCalculator.MaxCardioPoints) return null;
        return (Math.Max(0, minutes) / per + 1) * per;
    }

    /// <summary>How far toward the next point, 0 to 1; 1 once cardio earns no more.</summary>
    public static double ProgressToNextPoint(int seconds)
    {
        var per = PointsCalculator.CardioMinutesPerPoint * 60;
        if (NextPointAt(Minutes(seconds)) is null) return 1;
        return (double)(Math.Max(0, seconds) % per) / per;
    }

    private static int WholeSeconds(TimeSpan span) => span <= TimeSpan.Zero ? 0 : (int)span.TotalSeconds;
}
