namespace GymApp.Core;

/// <summary>What GainStreak writes to Apple Health (Phase 20).</summary>
public enum HealthKind { StrengthWorkout, CardioWorkout, Weight }

/// <summary>
/// One object GainStreak keeps in Apple Health. <see cref="SyncId"/> names it for good (Health replaces an object with
/// the same id and a higher <see cref="Version"/>); <see cref="Version"/> is 0 on what the app wants written and the
/// stored version on what Health already has. Times are local. <see cref="Value"/> is the weight in kg;
/// <see cref="Title"/> the workout's day name.
/// </summary>
public record HealthItem(string SyncId, HealthKind Kind, DateTime Start, DateTime End, double? Value = null,
    string? Title = null, long Version = 0)
{
    /// <summary>Same content, whatever the version: no write needed. Times to the second, since Health keeps them as
    /// seconds in floating point and the app's have milliseconds.</summary>
    public bool SameAs(HealthItem other) =>
        Kind == other.Kind && Seconds(Start) == Seconds(other.Start) && Seconds(End) == Seconds(other.End) && Title == other.Title &&
        Nullable.Equals(Value is { } v ? Math.Round(v, 2) : null, other.Value is { } o ? Math.Round(o, 2) : (double?)null);

    private static long Seconds(DateTime t) => (long)Math.Round(t.Ticks / (double)TimeSpan.TicksPerSecond);
}

/// <summary>What to change in Health: objects to save (with the version to save them at) and sync ids to delete.</summary>
public record HealthChanges(IReadOnlyList<HealthItem> Save, IReadOnlyList<HealthItem> Delete)
{
    public bool IsEmpty => Save.Count == 0 && Delete.Count == 0;
}

/// <summary>
/// Phase 20: the sync identifiers and the reconcile rule. HealthSync builds what Health should hold for a window of
/// days from the app's own records, reads what GainStreak already wrote there for the same account and window, and
/// applies the difference. Nothing is remembered locally: Undo, a deleted weigh-in or a failed write are all fixed by
/// the next reconcile.
/// </summary>
public static class HealthPlan
{
    public const string Prefix = "gainstreak-";

    /// <summary>The account's part of every id, so two accounts on one phone never touch each other's objects.</summary>
    public static string AccountPrefix(int userId) => $"{Prefix}u{userId}-";

    public static string WorkoutId(int userId, DateOnly date) => $"{AccountPrefix(userId)}workout-{date:yyyy-MM-dd}";

    public static string CardioId(int userId, DateOnly date, int sessionId) => $"{AccountPrefix(userId)}cardio-{date:yyyy-MM-dd}-{sessionId}";

    public static string WeightId(int userId, DateOnly date) => $"{AccountPrefix(userId)}weight-{date:yyyy-MM-dd}";

    /// <summary>Whether <paramref name="syncId"/> is one of this account's objects.</summary>
    public static bool IsOwn(string? syncId, int userId) => syncId?.StartsWith(AccountPrefix(userId), StringComparison.Ordinal) == true;

    /// <summary>
    /// The difference between <paramref name="wanted"/> (from the app) and <paramref name="existing"/> (this account's
    /// objects in Health, same window): new or changed objects are saved at a version above both the stored one and
    /// <paramref name="now"/>'s ticks, objects the app no longer has are deleted, the rest are left alone. Other
    /// accounts' and other apps' objects in <paramref name="existing"/> are ignored.
    /// </summary>
    public static HealthChanges Diff(int userId, IReadOnlyList<HealthItem> wanted, IReadOnlyList<HealthItem> existing, DateTime now)
    {
        var mine = existing.Where(e => IsOwn(e.SyncId, userId))
            .GroupBy(e => e.SyncId).ToDictionary(g => g.Key, g => g.MaxBy(e => e.Version)!);
        var save = new List<HealthItem>();
        foreach (var w in wanted.GroupBy(w => w.SyncId).Select(g => g.Last()))
        {
            mine.TryGetValue(w.SyncId, out var stored);
            if (stored is not null && stored.SameAs(w)) continue;
            save.Add(w with { Version = Math.Max(now.Ticks, (stored?.Version ?? 0) + 1) });
        }
        var wantedIds = wanted.Select(w => w.SyncId).ToHashSet();
        var delete = mine.Values.Where(e => !wantedIds.Contains(e.SyncId)).ToList();
        return new HealthChanges(save, delete);
    }
}
