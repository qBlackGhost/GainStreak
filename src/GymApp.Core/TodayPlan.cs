namespace GymApp.Core;

/// <summary>What the schedule says for one date. RotationIndex is the index into the split's day rotation
/// (null on a rest day); Title is the day's name, or "Rest day".</summary>
public record TodayWorkout(bool IsTraining, int? RotationIndex, string Title);

public static class TodayPlan
{
    /// <param name="week">A Monday-first week from <see cref="WeekSchedule.Build"/> or <see cref="WeekSchedule.FromWeekdays"/>.</param>
    /// <param name="dayNames">The split's rotation names in order (Push, Pull, Legs...).</param>
    public static TodayWorkout Resolve(IReadOnlyList<ScheduleSlot> week, IReadOnlyList<string> dayNames, DateOnly date)
    {
        var slot = week.FirstOrDefault(s => s.Weekday == date.DayOfWeek)
                   ?? throw new ArgumentException("The week must contain every weekday.", nameof(week));

        if (!slot.IsTraining || slot.RotationIndex is not { } index)
            return new TodayWorkout(false, null, "Rest day");

        var title = dayNames.Count == 0 ? "Workout" : dayNames[index % dayNames.Count];
        return new TodayWorkout(true, index, title);
    }
}
