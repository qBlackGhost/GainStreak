namespace GymApp.Core;

/// <summary>From <see cref="EffectiveFrom"/> on, the user's week looks like <see cref="WeekPattern"/>:
/// 7 chars of W (workout) / R (rest), Monday first.</summary>
public record ScheduleChange(DateOnly EffectiveFrom, string WeekPattern);

public enum StreakStatus
{
    NotStarted,    // no workout logged yet
    Active,        // streak alive, nothing owed, today is covered (rest day or workout logged)
    PendingToday,  // nothing owed, but today is a training day and nothing is logged yet
    AtRisk,        // yesterday's training day was missed and today isn't logged yet
    Shifted,       // today is logged but the schedule slipped a day, so a workout is owed tomorrow
    Broken         // count is 0 after a missed training day that was never made up
}

public record StreakResult(int Current, int Longest, StreakStatus Status);

/// <summary>How one scheduled training day turned out under the grace rule.</summary>
public enum DayOutcome
{
    Done,     // logged on its date
    Graced,   // missed, but paid by a later workout (shift rule)
    Missed,   // not paid, the streak broke on it (or it came before the first workout)
    Pending   // not settled yet: today or later, or still owed
}

public record TrainingDayOutcome(DateOnly Date, DayOutcome Outcome);

public static class StreakCalculator
{
    /// <summary>
    /// Recomputes the streak from scratch by walking forward day by day (grace rule, shift model).
    /// Rules:
    ///  - The walk starts on the first logged workout; nothing counts before it.
    ///  - Rest days count (+1), logged or not. A logged training day counts (+1).
    ///  - A missed training day is counted provisionally and leaves a debt. A workout the next day pays it;
    ///    if that next day is a training day, its own training becomes the new debt (the schedule shifts).
    ///    A day with an open debt and no workout breaks the streak (count 0); that day is consumed.
    ///  - Today is never a miss: an open debt makes it AtRisk, an unlogged training day PendingToday.
    ///  - Each day uses the schedule that was in force on that day. The first schedule entry also
    ///    covers days before its date. With no schedule at all, every day counts as a training day.
    ///  - Longest only takes settled counts (no debt open), plus the current count.
    /// </summary>
    public static StreakResult Calculate(
        IEnumerable<DateOnly> workoutDates,
        IReadOnlyList<ScheduleChange> schedule,
        DateOnly today) =>
        Replay(workoutDates, schedule, today, null);

    /// <summary>
    /// The outcome of every training day from <paramref name="from"/> to <paramref name="until"/>, from the
    /// same replay as <see cref="Calculate"/>. Training days before the first workout are Missed (nothing counts
    /// before it); days from today on, and a day still owed, are Pending.
    /// A workout that pays an owed day makes that day Graced. If it falls on a training day, that day's own
    /// training is owed next, so it ends up Graced (or Missed) rather than Done.
    /// </summary>
    public static IReadOnlyList<TrainingDayOutcome> Timeline(
        IEnumerable<DateOnly> workoutDates,
        IReadOnlyList<ScheduleChange> schedule,
        DateOnly today,
        DateOnly from,
        DateOnly until)
    {
        var settled = new Dictionary<DateOnly, DayOutcome>();
        Replay(workoutDates, schedule, today, settled);

        var ordered = schedule.OrderBy(s => s.EffectiveFrom).ToList();
        var result = new List<TrainingDayOutcome>();
        for (var d = from; d <= until; d = d.AddDays(1))
        {
            if (!IsTraining(ordered, d)) continue;
            var outcome = settled.TryGetValue(d, out var o) ? o
                        : d < today ? DayOutcome.Missed : DayOutcome.Pending;
            result.Add(new TrainingDayOutcome(d, outcome));
        }
        return result;
    }

    /// <summary>The walk behind both methods. When <paramref name="outcomes"/> is given, each training day the walk
    /// settles (Done, Graced, Missed) or leaves owed (Pending) is written to it.</summary>
    private static StreakResult Replay(
        IEnumerable<DateOnly> workoutDates,
        IReadOnlyList<ScheduleChange> schedule,
        DateOnly today,
        Dictionary<DateOnly, DayOutcome>? outcomes)
    {
        if (schedule.Any(s => !WeekSchedule.IsValidPattern(s.WeekPattern)))
            throw new ArgumentException("Schedule patterns must be 7 characters of W/R with at least one W.", nameof(schedule));

        var logged = workoutDates.Where(d => d <= today).ToHashSet();
        if (logged.Count == 0) return new StreakResult(0, 0, StreakStatus.NotStarted);

        var ordered = schedule.OrderBy(s => s.EffectiveFrom).ToList();

        var idx = 0;
        var count = 0;
        var debt = false;
        var owed = default(DateOnly);   // the training day the open debt is for
        var longest = 0;
        StreakStatus? stopped = null;

        for (var d = logged.Min(); d <= today; d = d.AddDays(1))
        {
            while (idx + 1 < ordered.Count && ordered[idx + 1].EffectiveFrom <= d) idx++;
            var isTraining = ordered.Count == 0 || WeekPattern.IsTrainingDay(ordered[idx].WeekPattern, d.DayOfWeek);
            var worked = logged.Contains(d);

            if (debt)
            {
                if (worked)                                                         // paid; a training day shifts
                {
                    count++;
                    Set(outcomes, owed, DayOutcome.Graced);
                    debt = isTraining;
                    owed = d;
                }
                else if (d == today) { stopped = StreakStatus.AtRisk; break; }
                else                                                                // break; day is consumed
                {
                    count = 0;
                    debt = false;
                    Set(outcomes, owed, DayOutcome.Missed);
                    if (isTraining) Set(outcomes, d, DayOutcome.Missed);
                }
            }
            else if (isTraining)
            {
                if (worked) { count++; Set(outcomes, d, DayOutcome.Done); }
                else if (d == today) { stopped = StreakStatus.PendingToday; break; }
                else { count++; debt = true; owed = d; }                            // provisional
            }
            else count++;                                                           // rest day

            if (!debt) longest = Math.Max(longest, count);
        }

        if (debt) Set(outcomes, owed, DayOutcome.Pending);

        longest = Math.Max(longest, count);
        var status = count == 0 ? StreakStatus.Broken
                   : stopped ?? (debt ? StreakStatus.Shifted : StreakStatus.Active);
        return new StreakResult(count, longest, status);
    }

    private static void Set(Dictionary<DateOnly, DayOutcome>? outcomes, DateOnly day, DayOutcome outcome)
    {
        if (outcomes is not null) outcomes[day] = outcome;
    }

    private static bool IsTraining(List<ScheduleChange> ordered, DateOnly d)
    {
        if (ordered.Count == 0) return true;
        var entry = ordered.LastOrDefault(s => s.EffectiveFrom <= d) ?? ordered[0];   // first entry covers earlier days
        return WeekPattern.IsTrainingDay(entry.WeekPattern, d.DayOfWeek);
    }
}
