namespace GymApp.Core;

/// <summary>One exercise on the day being checked in: how many working sets are planned, and the sets logged for it today.</summary>
public record CheckInExercise(int ExerciseId, int WorkingSets, IReadOnlyList<SetEntry> TodaySets);

/// <summary>How far today's check-in is. <see cref="CanLog"/> is false when the day has no exercises.</summary>
public record CheckInStatus(int Filled, int Required, int Total, bool CanLog);

/// <summary>When "I went today" may be logged: at least half of the day's exercises must have their sets filled in.</summary>
public static class CheckInRule
{
    // At least this share of the day's exercises must be filled, rounded up
    public const double MinFilledShare = 0.5;

    /// <summary>Filled exercises needed out of <paramref name="total"/>: half, rounded up (1 → 1, 3 → 2, 4 → 2, 5 → 3).</summary>
    public static int RequiredFor(int total) => (int)Math.Ceiling(total * MinFilledShare);

    /// <summary>Filled exercises a workout needs for full points: 70%, rounded up like <see cref="RequiredFor"/>
    /// (3 → 3, 4 → 3, 5 → 4, 6 → 5). Whole numbers, so 0.7 never rounds 7 of 10 up to 8.</summary>
    public static int RequiredForFull(int total) => (total * 7 + 9) / 10;

    /// <summary>An exercise is filled when it has at least its planned number of working sets today (and always at least one).
    /// Warm-ups don't count.</summary>
    public static bool IsFilled(CheckInExercise exercise) =>
        exercise.TodaySets.Count(s => s.Kind == SetKind.Working && s.Reps >= 1) >= Math.Max(1, exercise.WorkingSets);

    public static CheckInStatus Evaluate(IEnumerable<CheckInExercise> exercises)
    {
        var list = exercises.ToList();
        var filled = list.Count(IsFilled);
        var required = RequiredFor(list.Count);
        return new CheckInStatus(filled, required, list.Count, list.Count > 0 && filled >= required);
    }
}
