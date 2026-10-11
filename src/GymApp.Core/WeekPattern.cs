namespace GymApp.Core;

/// <summary>A resolved week as a 7-char string, Monday first: 'W' = workout day, 'R' = rest day.
/// This is what gets stored in the schedule history, so later edits to a split can't change the past.</summary>
public static class WeekPattern
{
    public static string From(IEnumerable<ScheduleSlot> slots) =>
        string.Concat(slots
            .OrderBy(s => ((int)s.Weekday + 6) % 7)
            .Select(s => s.IsTraining ? 'W' : 'R'));

    public static bool IsTrainingDay(string weekPattern, DayOfWeek day) =>
        weekPattern[((int)day + 6) % 7] == 'W';
}
