namespace GymApp.Core;

/// <summary>How a day shows in the dashboard's "This week" row.</summary>
public enum WeekChipKind
{
    Done,      // ✔ logged on its date
    MadeUp,    // ↺ missed, but made up by a later workout (Graced)
    Missed,    // ✘ not done (or still owed from a past day)
    Rest,      // a rest day
    Upcoming   // today or later, not settled yet
}

public record WeekChip(DateOnly Date, WeekChipKind Kind, bool IsToday);

public static class WeekRow
{
    /// <summary>
    /// The Monday-to-Sunday week containing <paramref name="today"/>, one chip per day, from a
    /// <see cref="StreakCalculator.Timeline"/> covering that week. Days not in the timeline are rest days.
    /// A Pending day is Upcoming from today on; before today it's owed, so it shows as Missed until it's made up.
    /// </summary>
    public static IReadOnlyList<WeekChip> Build(IReadOnlyList<TrainingDayOutcome> timeline, DateOnly today)
    {
        var monday = PointsCalculator.MondayOf(today);
        var byDate = timeline.ToDictionary(t => t.Date, t => t.Outcome);
        return Enumerable.Range(0, 7).Select(i =>
        {
            var d = monday.AddDays(i);
            var kind = !byDate.TryGetValue(d, out var o) ? WeekChipKind.Rest
                : o switch
                {
                    DayOutcome.Done => WeekChipKind.Done,
                    DayOutcome.Graced => WeekChipKind.MadeUp,
                    DayOutcome.Missed => WeekChipKind.Missed,
                    _ => d >= today ? WeekChipKind.Upcoming : WeekChipKind.Missed
                };
            return new WeekChip(d, kind, d == today);
        }).ToList();
    }
}
