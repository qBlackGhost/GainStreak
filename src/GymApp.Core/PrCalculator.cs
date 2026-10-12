namespace GymApp.Core;

public enum SetKind { Warmup, Working }

public record SetEntry(DateOnly Date, int SetNumber, SetKind Kind, int Reps, double WeightKg);

/// <summary>A manual correction. From Date on, this becomes the current best (it can go down).
/// OneRepMaxKg null = estimate with Epley.</summary>
public record PrAdjustment(DateOnly Date, double WeightKg, int Reps, double? OneRepMaxKg = null);

public enum PrEntryKind { Baseline, Pr, Manual }

public record PrEntry(DateOnly Date, double WeightKg, int Reps, double OneRepMaxKg, PrEntryKind Kind);

/// <summary>Entries are in date order; Current is the last one (the best right now).</summary>
public record PrHistory(PrEntry? Current, IReadOnlyList<PrEntry> Entries);

public static class PrCalculator
{
    private record Step(DateOnly Date, int Order, int Num, double WeightKg, int Reps, double? Manual1Rm, bool IsManual);

    /// <summary>The PR rule: a heavier weight always wins; at the same weight, more reps wins.
    /// Ties are not PRs. With no PR yet, anything is a (baseline) PR.</summary>
    public static bool Beats(double weightKg, int reps, PrEntry? best) =>
        best is null
        || weightKg > best.WeightKg
        || (weightKg == best.WeightKg && reps > best.Reps);

    /// <summary>
    /// Replays the working sets in date order. The first set is the baseline; after that, a set is a PR
    /// if it is heavier than the best, or equally heavy with more reps. Warmups never count.
    /// On the same day, sets are applied before adjustments.
    /// </summary>
    public static PrHistory Compute(IEnumerable<SetEntry> sets, IEnumerable<PrAdjustment>? adjustments = null)
    {
        var steps = sets.Where(s => s.Kind == SetKind.Working)
            .Select(s => new Step(s.Date, 0, s.SetNumber, s.WeightKg, s.Reps, null, false))
            .Concat((adjustments ?? []).Select(a => new Step(a.Date, 1, 0, a.WeightKg, a.Reps, a.OneRepMaxKg, true)))
            .OrderBy(s => s.Date).ThenBy(s => s.Order).ThenBy(s => s.Num);

        var entries = new List<PrEntry>();
        PrEntry? best = null;

        foreach (var s in steps)
        {
            var oneRm = s.Manual1Rm ?? OneRepMax.Epley(s.WeightKg, s.Reps);

            PrEntryKind kind;
            if (s.IsManual) kind = PrEntryKind.Manual;
            else if (best is null) kind = PrEntryKind.Baseline;
            else if (Beats(s.WeightKg, s.Reps, best)) kind = PrEntryKind.Pr;
            else continue;

            best = new PrEntry(s.Date, s.WeightKg, s.Reps, oneRm, kind);
            entries.Add(best);
        }

        return new PrHistory(best, entries);
    }

    /// <summary>Sets (warmups included) from the most recent day before <paramref name="before"/>,
    /// for prefilling the next session.</summary>
    public static IReadOnlyList<SetEntry> LastSession(IEnumerable<SetEntry> sets, DateOnly before)
    {
        var earlier = sets.Where(s => s.Date < before).ToList();
        if (earlier.Count == 0) return [];
        var day = earlier.Max(s => s.Date);
        return earlier.Where(s => s.Date == day).OrderBy(s => s.SetNumber).ToList();
    }
}