namespace GymApp.Core;

/// <summary>From <see cref="EffectiveFrom"/> on, the user's week looks like <see cref="WeekPattern"/>:
/// 7 chars of W (workout) / R (rest), Monday first.</summary>
public record ScheduleChange(DateOnly EffectiveFrom, string WeekPattern);

public enum StreakStatus
{
    NotStarted,    // no workout logged yet
    Active,        // streak alive and today is already covered (rest day or workout logged)
    PendingToday,  // streak alive, but today is a training day and nothing is logged yet
    AtRisk,        // reserved for the grace rule (a missed day that can still be made up); never returned yet
    Broken         // the streak is 0 after a missed training day; the next workout starts a new one
}

public record StreakResult(int Current, int Longest, StreakStatus Status);

public static class StreakCalculator
{
    /// <summary>
    /// Recomputes the streak from scratch, so backdated or deleted logs can never corrupt it.
    /// Rules:
    ///  - The streak counts calendar days and starts on the first logged workout.
    ///  - A day passes if a workout was logged (even on a rest day) or it is a rest day.
    ///  - A training day with no workout breaks the streak once that day is over; today stays pending.
    ///  - Rest days after a break do not restart anything; only a workout does.
    ///  - Each day uses the schedule that was in force on that day. The first schedule entry also
    ///    covers days before its date. With no schedule at all, every day counts as a training day.
    /// </summary>
    /// <param name="graceDays">Reserved for the "make it up the next day" rule. Must be 0 for now.</param>
    public static StreakResult Calculate(
        IEnumerable<DateOnly> workoutDates,
        IReadOnlyList<ScheduleChange> schedule,
        DateOnly today,
        int graceDays = 0)
    {
        if (graceDays != 0)
            throw new NotSupportedException("The grace rule is not implemented yet.");
        if (schedule.Any(s => !WeekSchedule.IsValidPattern(s.WeekPattern)))
            throw new ArgumentException("Schedule patterns must be 7 characters of W/R with at least one W.", nameof(schedule));

        var logged = workoutDates.Where(d => d <= today).ToHashSet();
        if (logged.Count == 0) return new StreakResult(0, 0, StreakStatus.NotStarted);

        var ordered = schedule.OrderBy(s => s.EffectiveFrom).ToList();
        var idx = 0;
        var running = 0;
        var longest = 0;
        var todayPending = false;

        for (var d = logged.Min(); d <= today; d = d.AddDays(1))
        {
            while (idx + 1 < ordered.Count && ordered[idx + 1].EffectiveFrom <= d) idx++;
            var isTraining = ordered.Count == 0 || WeekPattern.IsTrainingDay(ordered[idx].WeekPattern, d.DayOfWeek);

            if (logged.Contains(d)) running++;
            else if (!isTraining) { if (running > 0) running++; }
            else if (d != today) running = 0;        // missed training day (today is still pending)

            longest = Math.Max(longest, running);
            if (d == today) todayPending = isTraining && !logged.Contains(d);
        }

        var status = running == 0 ? StreakStatus.Broken
                   : todayPending ? StreakStatus.PendingToday
                   : StreakStatus.Active;
        return new StreakResult(running, longest, status);
    }
}
