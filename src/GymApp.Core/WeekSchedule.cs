namespace GymApp.Core;

/// <summary>One cell of the weekly grid. RotationIndex is the zero-based index into the split's
/// day rotation (Push = 0, Pull = 1, ...), or null on a rest day.</summary>
public record ScheduleSlot(DayOfWeek Weekday, bool IsTraining, int? RotationIndex);

public static class WeekSchedule
{
    public static bool IsValidPattern(string? pattern) =>
        pattern is { Length: 7 } && pattern.All(c => c is 'W' or 'R') && pattern.Contains('W');

    /// <summary>
    /// Presets: pattern is 7 chars of 'W' (workout) / 'R' (rest) starting on <paramref name="startDay"/>.
    /// The n-th workout maps to the n-th day of the rotation (cycling if the rotation is shorter).
    /// The result is always Monday-first, so it can be shown as a normal calendar week.
    /// </summary>
    public static IReadOnlyList<ScheduleSlot> Build(string pattern, DayOfWeek startDay, int rotationLength)
    {
        if (!IsValidPattern(pattern))
            throw new ArgumentException("Pattern must be 7 characters of W/R with at least one W.", nameof(pattern));
        if (rotationLength < 1)
            throw new ArgumentOutOfRangeException(nameof(rotationLength));

        var slots = new List<ScheduleSlot>(7);
        var n = 0;
        for (var i = 0; i < 7; i++)
        {
            var weekday = (DayOfWeek)(((int)startDay + i) % 7);
            slots.Add(pattern[i] == 'W'
                ? new ScheduleSlot(weekday, true, n++ % rotationLength)
                : new ScheduleSlot(weekday, false, null));
        }
        return slots.OrderBy(s => ((int)s.Weekday + 6) % 7).ToList();
    }

    /// <summary>Custom splits: the user picked the weekdays directly. Monday-first, rotation index = n-th training day.</summary>
    public static IReadOnlyList<ScheduleSlot> FromWeekdays(IEnumerable<DayOfWeek> trainingDays)
    {
        var set = trainingDays.ToHashSet();
        if (set.Count == 0)
            throw new ArgumentException("At least one training day is required.", nameof(trainingDays));

        var slots = new List<ScheduleSlot>(7);
        var n = 0;
        for (var i = 0; i < 7; i++)
        {
            var weekday = (DayOfWeek)((i + 1) % 7);   // Monday, Tuesday, ... Sunday
            slots.Add(set.Contains(weekday)
                ? new ScheduleSlot(weekday, true, n++)
                : new ScheduleSlot(weekday, false, null));
        }
        return slots;
    }

    /// <summary>What the streak logic (4b) will need: which weekdays are training days.</summary>
    public static IReadOnlyList<DayOfWeek> TrainingWeekdays(IEnumerable<ScheduleSlot> slots) =>
        slots.Where(s => s.IsTraining).Select(s => s.Weekday).ToList();
}
