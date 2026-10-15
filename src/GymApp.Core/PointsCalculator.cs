using System.Globalization;

namespace GymApp.Core;

/// <summary>Why points were given. Stored as its name in the database, so don't rename the members.</summary>
public enum PointsReason { Workout, WeekComplete, WeekCompleteGrace, CalorieTarget }

/// <summary>One ledger entry. <see cref="DedupKey"/> is unique per user, so a day or week can only earn once.</summary>
public record PointsAward(PointsReason Reason, int Amount, DateOnly Date, string DedupKey);

/// <summary>A calorie log with the goal and goal type that were in force when it was saved.</summary>
public record CalorieDay(DateOnly Date, int Consumed, int? Goal, GoalType? GoalType);

/// <summary>Points rules. Points come only from "I went today" logs, full weeks and calorie targets,
/// never from sets or PRs.</summary>
public static class PointsCalculator
{
    public const int WorkoutPoints = 5;
    public const int WeekCompletePoints = 50;
    public const int WeekCompleteGracePoints = 25;
    public const int CalorieTargetPoints = 10;

    /// <summary>A calorie log only counts when it is at least this share of the goal, so logging 0 kcal never hits.</summary>
    public const double CalorieMinimumShare = 0.5;

    /// <summary>
    /// The entries that should exist from <paramref name="windowStart"/> to <paramref name="today"/>.
    /// Rules:
    ///  - +5 for each day with a workout log.
    ///  - Weeks run Monday to Sunday and only count when the user had a schedule on all seven days
    ///    (Monday on or after <paramref name="scheduleStart"/>) and the week has a training day.
    ///    All training days Done: +50, dated the last training day, even before Sunday.
    ///    All Done or Graced with at least one Graced: +25, dated Sunday, only once the week has ended.
    ///    Anything Missed or still Pending: nothing (yet). Both bonuses share one key, so a week earns one of them.
    ///  - +10 for each calorie log that hits its target (see <see cref="CalorieGoals.HitTarget"/>) and is at least
    ///    half the goal. The goal and goal type are the ones saved with the log.
    /// </summary>
    /// <param name="timeline">From <see cref="StreakCalculator.Timeline"/>, covering at least every day of every
    /// week in the window, including the rest of the current week.</param>
    /// <param name="scheduleStart">The first day the user had a schedule, or null if they never had one.</param>
    public static IReadOnlyList<PointsAward> Calculate(
        IEnumerable<DateOnly> workoutDates,
        IReadOnlyList<TrainingDayOutcome> timeline,
        DateOnly? scheduleStart,
        IEnumerable<CalorieDay> calories,
        DateOnly today,
        DateOnly windowStart)
    {
        bool InWindow(DateOnly d) => d >= windowStart && d <= today;
        var awards = new List<PointsAward>();

        foreach (var d in workoutDates.Where(InWindow).Distinct().Order())
            awards.Add(new(PointsReason.Workout, WorkoutPoints, d, $"workout:{Key(d)}"));

        for (var monday = FirstMondayOnOrAfter(windowStart); monday <= today; monday = monday.AddDays(7))
            if (Week(monday, timeline, scheduleStart, today) is { } bonus)
                awards.Add(bonus);

        foreach (var c in calories.Where(c => InWindow(c.Date)).DistinctBy(c => c.Date).OrderBy(c => c.Date))
            if (HitsTarget(c))
                awards.Add(new(PointsReason.CalorieTarget, CalorieTargetPoints, c.Date, $"calories:{Key(c.Date)}"));

        return awards;
    }

    public static bool HitsTarget(CalorieDay c) =>
        c is { Goal: { } goal and > 0, GoalType: { } type }
        && c.Consumed >= goal * CalorieMinimumShare
        && CalorieGoals.HitTarget(type, goal, c.Consumed);

    private static PointsAward? Week(DateOnly monday, IReadOnlyList<TrainingDayOutcome> timeline, DateOnly? scheduleStart, DateOnly today)
    {
        if (scheduleStart is not { } start || start > monday) return null;   // partial first week

        var sunday = monday.AddDays(6);
        var days = timeline.Where(t => t.Date >= monday && t.Date <= sunday).ToList();
        if (days.Count == 0) return null;

        var key = $"week:{Key(monday)}";
        if (days.All(t => t.Outcome == DayOutcome.Done))
            return new(PointsReason.WeekComplete, WeekCompletePoints, days.Max(t => t.Date), key);
        if (today > sunday && days.All(t => t.Outcome is DayOutcome.Done or DayOutcome.Graced))
            return new(PointsReason.WeekCompleteGrace, WeekCompleteGracePoints, sunday, key);
        return null;
    }

    /// <summary>The Monday of the week containing <paramref name="d"/>.</summary>
    public static DateOnly MondayOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private static DateOnly FirstMondayOnOrAfter(DateOnly d)
    {
        var monday = MondayOf(d);
        return monday < d ? monday.AddDays(7) : monday;
    }

    private static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
