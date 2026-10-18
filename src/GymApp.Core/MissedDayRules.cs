using System.Globalization;

namespace GymApp.Core;

/// <summary>One day of a week preview: "Fri · Rest" or "Wed · Pull".</summary>
public record WeekPreviewDay(DayOfWeek Weekday, bool IsTraining, string Title);

/// <summary>
/// The rules and texts behind the missed-day choice. Pure logic: the service passes in the user's current week
/// (Monday-first, from <see cref="WeekSchedule"/>) and the split's day names.
/// </summary>
public static class MissedDayRules
{
    /// <summary>"Shift my whole schedule by a day" uses per Monday-to-Sunday week, counted by the missed day's week.</summary>
    public const int MaxWeekShiftsPerWeek = 1;

    /// <summary>"Make it up before my next rest day" uses per Monday-to-Sunday week, counted by the missed day's week.</summary>
    public const int MaxMakeUpsPerWeek = 2;

    /// <summary>How many uses of <paramref name="choice"/> are left in the week of <paramref name="missed"/>,
    /// given the user's stored choices (keyed by missed date). Continue has no limit.</summary>
    public static int Left(MissChoice choice, DateOnly missed, IReadOnlyDictionary<DateOnly, MissChoice> choices)
    {
        var max = choice switch
        {
            MissChoice.ShiftWeek => MaxWeekShiftsPerWeek,
            MissChoice.ShiftToRest => MaxMakeUpsPerWeek,
            _ => int.MaxValue
        };
        if (max == int.MaxValue) return max;

        var monday = PointsCalculator.MondayOf(missed);
        var used = choices.Count(c => c.Value == choice && c.Key >= monday && c.Key <= monday.AddDays(6));
        return Math.Max(0, max - used);
    }

    /// <summary>The week moved one day later: each weekday gets the slot of the day before it (Sunday wraps to Monday).</summary>
    public static IReadOnlyList<ScheduleSlot> ShiftedWeek(IReadOnlyList<ScheduleSlot> week) =>
        week.Select(s => week.Single(p => p.Weekday == Previous(s.Weekday)) with { Weekday = s.Weekday })
            .OrderBy(s => ((int)s.Weekday + 6) % 7)
            .ToList();

    /// <summary>The 7 days from <paramref name="from"/> on, for the "new week" row.</summary>
    public static IReadOnlyList<WeekPreviewDay> Preview(IReadOnlyList<ScheduleSlot> week, IReadOnlyList<string> dayNames, DateOnly from) =>
        Enumerable.Range(0, 7).Select(i =>
        {
            var w = TodayPlan.Resolve(week, dayNames, from.AddDays(i));
            return new WeekPreviewDay(from.AddDays(i).DayOfWeek, w.IsTraining, w.IsTraining ? w.Title : "Rest");
        }).ToList();

    public static bool HasRestDay(IReadOnlyList<ScheduleSlot> week) => week.Any(s => !s.IsTraining);

    /// <summary>"Pull today, and every day after moves one day later. Your rest day moves from Thursday to Friday."</summary>
    public static string ShiftWeekText(IReadOnlyList<ScheduleSlot> week, IReadOnlyList<string> dayNames, DateOnly missed)
    {
        var name = TodayPlan.Resolve(week, dayNames, missed).Title;
        var text = $"{name} today, and every day after moves one day later.";
        var rest = week.Where(s => !s.IsTraining).Select(s => s.Weekday).ToList();
        if (rest.Count == 0) return text;

        var moved = rest.Select(Next).OrderBy(d => ((int)d + 6) % 7).ToList();
        return rest.Count == 1
            ? $"{text} Your rest day moves from {Name(rest[0])} to {Name(moved[0])}."
            : $"{text} Your rest days move from {Join(rest)} to {Join(moved)}.";
    }

    /// <summary>"Pull today, Legs on Thursday instead of resting, back to normal on Friday." Each day from today does
    /// the day before's workout, until the next rest day absorbs the slip. Needs a week with a rest day.</summary>
    public static string MakeUpText(IReadOnlyList<ScheduleSlot> week, IReadOnlyList<string> dayNames, DateOnly today)
    {
        var parts = new List<string>();
        for (var d = today; d < today.AddDays(8); d = d.AddDays(1))
        {
            var owed = TodayPlan.Resolve(week, dayNames, d.AddDays(-1)).Title;
            var isRest = !TodayPlan.Resolve(week, dayNames, d).IsTraining;
            var when = d == today ? "today" : $"on {Name(d.DayOfWeek)}";
            parts.Add(isRest ? $"{owed} {when} instead of resting" : $"{owed} {when}");
            if (isRest)
                return $"{string.Join(", ", parts)}, back to normal on {Name(d.AddDays(1).DayOfWeek)}.";
        }
        throw new ArgumentException("The week needs a rest day.", nameof(week));
    }

    /// <summary>"Today stays Legs. Your streak resets to 0."</summary>
    public static string ContinueText(IReadOnlyList<ScheduleSlot> week, IReadOnlyList<string> dayNames, DateOnly today)
    {
        var w = TodayPlan.Resolve(week, dayNames, today);
        return w.IsTraining
            ? $"Today stays {w.Title}. Your streak resets to 0."
            : "Today stays a rest day. Your streak resets to 0.";
    }

    /// <summary>Why a used-up option is hidden, e.g. "You've already made up 2 workouts this week."</summary>
    public static string UsedUpText(MissChoice choice) => choice switch
    {
        MissChoice.ShiftWeek => MaxWeekShiftsPerWeek == 1
            ? "You've already shifted your schedule this week."
            : $"You've already shifted your schedule {MaxWeekShiftsPerWeek} times this week.",
        MissChoice.ShiftToRest => $"You've already made up {MaxMakeUpsPerWeek} workouts this week.",
        _ => ""
    };

    private static DayOfWeek Previous(DayOfWeek d) => (DayOfWeek)(((int)d + 6) % 7);
    private static DayOfWeek Next(DayOfWeek d) => (DayOfWeek)(((int)d + 1) % 7);
    private static string Name(DayOfWeek d) => CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(d);

    private static string Join(IReadOnlyList<DayOfWeek> days) =>
        days.Count == 1 ? Name(days[0])
        : string.Join(", ", days.Take(days.Count - 1).Select(Name)) + " and " + Name(days[^1]);
}
