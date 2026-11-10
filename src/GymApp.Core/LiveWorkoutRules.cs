namespace GymApp.Core;

/// <summary>One exercise of a live workout's day, in plan order: its planned sets and the sets logged for it today.</summary>
public record LiveExercise(int ExerciseId, int WarmupSets, int WorkingSets, IReadOnlyList<SetEntry> TodaySets)
{
    public CheckInExercise ToRuleInput() => new(ExerciseId, WorkingSets, TodaySets);

    public bool IsFilled => CheckInRule.IsFilled(ToRuleInput());

    /// <summary>The planned working sets; at least one, like <see cref="CheckInRule.IsFilled"/>.</summary>
    public int PlannedWorking => Math.Max(1, WorkingSets);

    public int LoggedWarmups => TodaySets.Count(s => s.Kind == SetKind.Warmup);
    public int LoggedWorking => TodaySets.Count(s => s.Kind == SetKind.Working);
}

/// <summary>The set to log next on the card: its kind and its 1-based number within that kind.</summary>
public record LiveSet(SetKind Kind, int Number);

/// <summary>How a live workout ended: <see cref="Full"/> with 70% or more of the exercises filled,
/// <see cref="Half"/> with 50% to 69% (both already logged), <see cref="NotCounted"/> below half.</summary>
public enum LiveOutcome { NotCounted, Half, Full }

/// <summary>Phase 18's live workout: which exercise and set the card shows, and when a session stops by itself.</summary>
public static class LiveWorkoutRules
{
    /// <summary>A session stops this long after it started.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(4);

    /// <summary>Away from the app this long with a set logged: "Continue your workout?" (the notification, and the popup
    /// on coming back).</summary>
    public static readonly TimeSpan NudgeAfter = TimeSpan.FromMinutes(12);

    /// <summary>The next set of <paramref name="exercise"/>: warm-ups first, then working sets. Null once the exercise
    /// is filled, even when a planned warm-up was left out (sets from the Day screen).</summary>
    public static LiveSet? NextSet(LiveExercise exercise)
    {
        if (exercise.IsFilled) return null;
        if (exercise.LoggedWarmups < exercise.WarmupSets) return new LiveSet(SetKind.Warmup, exercise.LoggedWarmups + 1);
        return new LiveSet(SetKind.Working, exercise.LoggedWorking + 1);
    }

    /// <summary>
    /// The exercise on the card: the one the user picked (<paramref name="currentExerciseId"/>, even a filled one,
    /// to change its sets), else the first in plan order that isn't filled or skipped, then the skipped ones in the
    /// order they were skipped. Null when every exercise is filled.
    /// </summary>
    public static LiveExercise? Current(IReadOnlyList<LiveExercise> plan, int? currentExerciseId,
        IReadOnlyList<int> skippedExerciseIds)
    {
        if (currentExerciseId is { } picked && plan.FirstOrDefault(e => e.ExerciseId == picked) is { } chosen) return chosen;
        return plan.FirstOrDefault(e => !e.IsFilled && !skippedExerciseIds.Contains(e.ExerciseId))
               ?? skippedExerciseIds.Select(id => plan.FirstOrDefault(e => e.ExerciseId == id))
                   .FirstOrDefault(e => e is { IsFilled: false })
               ?? plan.FirstOrDefault(e => !e.IsFilled);
    }

    /// <summary>The exercises in the order the card works through them: the plan's order with skipped ones moved to
    /// the end, in the order they were skipped.</summary>
    public static IReadOnlyList<LiveExercise> Order(IReadOnlyList<LiveExercise> plan, IReadOnlyList<int> skippedExerciseIds)
    {
        var skipped = skippedExerciseIds.Select(id => plan.FirstOrDefault(e => e.ExerciseId == id)).OfType<LiveExercise>().ToList();
        return plan.Where(e => !skipped.Contains(e)).Concat(skipped).ToList();
    }

    /// <summary>A session must stop 4 hours after it started, or once the day changed (midnight).</summary>
    public static bool MustStop(DateTime startedAt, DateOnly date, DateTime now) => StopAt(startedAt, date) <= now;

    /// <summary>When a session stops by itself: 4 hours after it started, or at midnight, whichever comes first.</summary>
    public static DateTime StopAt(DateTime startedAt, DateOnly date)
    {
        var fourHours = startedAt + MaxLength;
        var midnight = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return fourHours < midnight ? fourHours : midnight;
    }

    /// <summary>Whether the stop at <see cref="StopAt"/> is the midnight one.</summary>
    public static bool StopsAtMidnight(DateTime startedAt, DateOnly date) =>
        StopAt(startedAt, date) == date.AddDays(1).ToDateTime(TimeOnly.MinValue);

    /// <summary>
    /// Working sets done out of the planned ones, for the iPhone's Live Activity (0.20.0): each exercise counts up to
    /// its planned working sets, counted the way <see cref="CheckInRule.IsFilled"/> does (a rep or more). Warm-ups and
    /// extra sets don't count, so a filled exercise counts exactly its plan.
    /// </summary>
    public static (int Done, int Planned) SetProgress(IReadOnlyList<LiveExercise> plan) => (
        plan.Sum(e => Math.Min(e.TodaySets.Count(s => s.Kind == SetKind.Working && s.Reps >= 1), e.PlannedWorking)),
        plan.Sum(e => e.PlannedWorking));

    /// <summary>The whole percent of <paramref name="done"/> out of <paramref name="planned"/>, rounded down, so 100 means
    /// every set is done.</summary>
    public static int Percent(int done, int planned) => planned <= 0 ? 0 : Math.Clamp(done * 100 / planned, 0, 100);

    public static LiveOutcome Outcome(int filled, int total) =>
        total == 0 || filled < CheckInRule.RequiredFor(total) ? LiveOutcome.NotCounted
        : filled >= CheckInRule.RequiredForFull(total) ? LiveOutcome.Full
        : LiveOutcome.Half;

    /// <summary>"12,7" → [12, 7]; anything that isn't a number is dropped.</summary>
    public static IReadOnlyList<int> ParseIds(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => int.TryParse(p, out var id) ? id : (int?)null).OfType<int>().Distinct().ToList();

    public static string FormatIds(IEnumerable<int> ids) => string.Join(",", ids.Distinct());
}
