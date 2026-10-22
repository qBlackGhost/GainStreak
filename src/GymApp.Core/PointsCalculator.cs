using System.Globalization;

namespace GymApp.Core;

/// <summary>Why points were given. Stored as its name in the database, so don't rename the members.
/// Adjustment is a manual entry from the TestBench; the sync never adds or removes those.</summary>
public enum PointsReason { Workout, WeekComplete, WeekCompleteGrace, CalorieTarget, Adjustment, Cardio }

/// <summary>One ledger entry. <see cref="DedupKey"/> is unique per user, so a day or week can only earn once.</summary>
public record PointsAward(PointsReason Reason, int Amount, DateOnly Date, string DedupKey);

/// <summary>A calorie log with the goal that was in force when it was saved.</summary>
public record CalorieRecord(DateOnly Date, int Consumed, int? Goal);

/// <summary>The optional cardio minutes saved with a day's workout log.</summary>
public record CardioRecord(DateOnly Date, int Minutes);

/// <summary>Points rules. Points come only from "I went today" logs (and the cardio logged with them), full weeks
/// and calorie targets, never from sets or PRs.</summary>
public static class PointsCalculator
{
    public const int WorkoutPoints = 5;
    public const int WeekCompletePoints = 50;
    public const int WeekCompleteGracePoints = 25;
    public const int CalorieTargetPoints = 10;
    /// <summary>Cardio earns 1 point per full block of this many minutes: 29 minutes is +2.</summary>
    public const int CardioMinutesPerPoint = 10;
    /// <summary>The most cardio can earn in one session, so it never outweighs the workout itself.</summary>
    public const int MaxCardioPoints = 3;
    /// <summary>The most cardio minutes one check-in accepts.</summary>
    public const int MaxCardioMinutes = 600;

    public static int CardioPoints(int minutes) => Math.Min(MaxCardioPoints, Math.Max(0, minutes) / CardioMinutesPerPoint);

    /// <summary>The name the UI shows for a reason, e.g. "Week complete (grace)" for the 25-point bonus.</summary>
    public static string DisplayName(PointsReason reason) => reason switch
    {
        PointsReason.Workout => "Workout",
        PointsReason.WeekComplete => "Week complete",
        PointsReason.WeekCompleteGrace => "Week complete (grace)",
        PointsReason.CalorieTarget => "Calorie target",
        PointsReason.Adjustment => "Manual adjustment",
        PointsReason.Cardio => "Cardio",
        _ => reason.ToString()
    };

    /// <summary>
    /// The entries that should exist from <paramref name="windowStart"/> to <paramref name="today"/>.
    /// Rules:
    ///  - +5 for each day with a workout log.
    ///  - +1 per full 10 minutes of cardio saved with that day's log (see <see cref="CardioPoints"/>), at most +3; under 10 earns nothing.
    ///  - Weeks run Monday to Sunday and only count when the user had a schedule on all seven days
    ///    (Monday on or after <paramref name="scheduleStart"/>) and the week has a training day.
    ///    All training days Done: +50, dated the last training day, even before Sunday.
    ///    All Done or Graced with at least one Graced: +25, dated Sunday, only once the week has ended.
    ///    Anything Missed or still Pending: nothing (yet). Both bonuses share one key, so a week earns one of them.
    ///  - +10 for each calorie log within the leeway of its goal (see <see cref="CalorieGoals.HitTarget"/>).
    ///    The goal is the one saved with the log.
    /// </summary>
    /// <param name="timeline">From <see cref="StreakCalculator.Timeline"/>, covering at least every day of every
    /// week in the window, including the rest of the current week.</param>
    /// <param name="scheduleStart">The first day the user had a schedule, or null if they never had one.</param>
    public static IReadOnlyList<PointsAward> Calculate(
        IEnumerable<DateOnly> workoutDates,
        IReadOnlyList<TrainingDayOutcome> timeline,
        DateOnly? scheduleStart,
        IEnumerable<CalorieRecord> calories,
        DateOnly today,
        DateOnly windowStart,
        IEnumerable<CardioRecord>? cardio = null)
    {
        bool InWindow(DateOnly d) => d >= windowStart && d <= today;
        var awards = new List<PointsAward>();

        foreach (var d in workoutDates.Where(InWindow).Distinct().Order())
            awards.Add(new(PointsReason.Workout, WorkoutPoints, d, $"workout:{Key(d)}"));

        foreach (var c in (cardio ?? []).Where(c => InWindow(c.Date)).DistinctBy(c => c.Date).OrderBy(c => c.Date))
            if (CardioPoints(c.Minutes) is var p and > 0)
                awards.Add(new(PointsReason.Cardio, p, c.Date, $"cardio:{Key(c.Date)}"));

        for (var monday = FirstMondayOnOrAfter(windowStart); monday <= today; monday = monday.AddDays(7))
            if (Week(monday, timeline, scheduleStart, today) is { } bonus)
                awards.Add(bonus);

        foreach (var c in calories.Where(c => InWindow(c.Date)).DistinctBy(c => c.Date).OrderBy(c => c.Date))
            if (HitsTarget(c))
                awards.Add(new(PointsReason.CalorieTarget, CalorieTargetPoints, c.Date, $"calories:{Key(c.Date)}"));

        return awards;
    }

    public static bool HitsTarget(CalorieRecord c) =>
        c.Goal is { } goal and > 0 && CalorieGoals.HitTarget(goal, c.Consumed);

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
