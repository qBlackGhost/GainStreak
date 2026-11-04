namespace GymApp.Core;

/// <summary>What friends would see for the user on <see cref="Date"/> if nothing new is logged before then.
/// <see cref="WeekPoints"/> is for the Monday-to-Sunday week containing <see cref="Date"/>.</summary>
public sealed record ProjectedStats(DateOnly Date, int TotalPoints, int WeekPoints, int CurrentStreak);

/// <summary>
/// The user's public stats for today and the next days, assuming no more workouts or calorie logs. A server can't
/// replay a friend's logs, yet a streak grows on rest days and breaks after a missed day without the app being
/// opened, and a grace week's bonus is paid once the week ends. So the phone uploads this list and the server shows
/// the entry for the viewer's day.
/// </summary>
public static class StatsProjection
{
    public const int DefaultDays = 14;

    /// <summary>
    /// One entry per day from <paramref name="today"/> to <paramref name="today"/> + <paramref name="days"/> - 1.
    /// Each day replays what the app does when opened on it: the points sync (entries before its window are
    /// frozen, manual adjustments are kept, the rest follows <see cref="PointsCalculator"/>), then the streak.
    /// Days are walked in order, so a bonus frozen on one day stays on the next, like the real ledger.
    /// Pure: nothing is written, and missed days have no stored choice (they behave like ShiftToRest).
    /// </summary>
    /// <param name="ledger">The stored points entries, current as of <paramref name="today"/> (synced).</param>
    /// <param name="calories">Calorie logs from the previous week's Monday on; earlier ones can't change anything.</param>
    /// <param name="cardio">Cardio minutes from workout logs, over the same range as <paramref name="calories"/>.</param>
    /// <param name="workouts">Completion and verification of the workout logs over the same range (Phase 18), so a
    /// partial or unverified workout projects its 2 points, as the real ledger has them.</param>
    public static IReadOnlyList<ProjectedStats> Project(
        IReadOnlyCollection<DateOnly> workoutDates,
        IReadOnlyList<ScheduleChange> schedule,
        IReadOnlyDictionary<DateOnly, MissChoice> choices,
        IReadOnlyCollection<CalorieRecord> calories,
        IReadOnlyList<PointsAward> ledger,
        DateOnly today,
        int days = DefaultDays,
        IReadOnlyCollection<CardioRecord>? cardio = null,
        IReadOnlyCollection<WorkoutRecord>? workouts = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

        DateOnly? scheduleStart = schedule.Count == 0 ? null : schedule[0].EffectiveFrom;
        var entries = ledger.ToList();
        var result = new List<ProjectedStats>(days);

        for (var day = today; day < today.AddDays(days); day = day.AddDays(1))
        {
            entries = Sync(entries, workoutDates, schedule, choices, calories, cardio ?? [], workouts ?? [], scheduleStart, day);

            var monday = PointsCalculator.MondayOf(day);
            var sunday = monday.AddDays(6);
            var week = entries.Where(e => e.Date >= monday && e.Date <= sunday).Sum(e => e.Amount);
            var streak = StreakCalculator.Calculate(workoutDates, schedule, choices, day).Current;
            result.Add(new ProjectedStats(day, entries.Sum(e => e.Amount), week, streak));
        }
        return result;
    }

    /// <summary>The ledger after a points sync on <paramref name="day"/>, as PointsService does it.</summary>
    private static List<PointsAward> Sync(
        List<PointsAward> entries, IReadOnlyCollection<DateOnly> workoutDates, IReadOnlyList<ScheduleChange> schedule,
        IReadOnlyDictionary<DateOnly, MissChoice> choices, IReadOnlyCollection<CalorieRecord> calories,
        IReadOnlyCollection<CardioRecord> cardio, IReadOnlyCollection<WorkoutRecord> workouts, DateOnly? scheduleStart, DateOnly day)
    {
        var windowStart = PointsCalculator.MondayOf(day).AddDays(-7);   // PointsService.WindowStart
        var timeline = StreakCalculator.Timeline(workoutDates, schedule, choices, day,
            windowStart, PointsCalculator.MondayOf(day).AddDays(6));
        var wanted = PointsCalculator.Calculate(workoutDates, timeline, scheduleStart, calories, day, windowStart, cardio, workouts);

        return entries
            .Where(e => e.Date < windowStart || e.Reason == PointsReason.Adjustment)
            .Concat(wanted)
            .ToList();
    }
}
