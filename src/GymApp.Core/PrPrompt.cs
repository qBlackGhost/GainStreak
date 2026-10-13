using System.Globalization;

namespace GymApp.Core;

public enum PrStatus { Info, Below, Tie, NewPr }

public record PrPrompt(string Text, PrStatus Status);

/// <summary>One row of today's draft on the exercise screen. Null = not typed yet (or not a number).</summary>
public record DraftSet(SetKind Kind, double? WeightKg, int? Reps);

public static class PrPrompts
{
    /// <param name="pr">The current best (PrHistory.Current), or null if there is none yet.</param>
    /// <param name="weightKg">What is typed in the weight field so far, null if empty.</param>
    /// <param name="reps">What is typed in the reps field so far, null if empty.</param>
    public static PrPrompt For(PrEntry? pr, double? weightKg, int? reps = null)
    {
        if (pr is null)
            return new("No PR yet. Your first working set sets it.", PrStatus.Info);

        var label = $"PR: {Kg(pr.WeightKg)} × {pr.Reps}";
        if (weightKg is not { } w)
            return new(label, PrStatus.Info);

        if (w > pr.WeightKg)
            return new($"{label}. {Kg(w)} is heavier, so any reps is a new PR.", PrStatus.NewPr);

        if (w < pr.WeightKg)
            return new($"{label}. Go above {Kg(pr.WeightKg)} to beat it.", PrStatus.Below);

        // same weight: reps decide
        if (reps is not { } r)
            return new($"{label}. Do {pr.Reps + 1}+ reps at {Kg(w)} for a new PR.", PrStatus.Info);
        if (r > pr.Reps)
            return new($"{label}. New PR!", PrStatus.NewPr);
        if (r == pr.Reps)
            return new($"{label}. That ties your PR, one more rep beats it.", PrStatus.Tie);
        return new($"{label}. {pr.Reps + 1 - r} more rep(s) to beat it.", PrStatus.Below);
    }

    /// <summary>
    /// The prompt for row <paramref name="index"/> of today's draft; null for warm-up rows (they never count).
    /// The best it is compared to = sets before today + the working rows above it + any adjustment up to
    /// today. Sets already saved today are ignored, because the draft replaces them when saved.
    /// </summary>
    public static PrPrompt? ForRow(
        IReadOnlyList<DraftSet> rows, int index,
        IEnumerable<SetEntry> savedSets, IEnumerable<PrAdjustment> adjustments, DateOnly today)
    {
        if (index < 0 || index >= rows.Count || rows[index].Kind != SetKind.Working) return null;

        var above = rows.Take(index)
            .Where(r => r.Kind == SetKind.Working && r.WeightKg is not null && r.Reps is > 0)
            .Select((r, i) => new SetEntry(today, i + 1, SetKind.Working, r.Reps!.Value, r.WeightKg!.Value))
            .ToList();

        var best = PrCalculator.Compute(
            savedSets.Where(s => s.Date < today).Concat(above),
            adjustments.Where(a => a.Date <= today)).Current;

        var row = rows[index];
        return For(best, row.WeightKg, row.Reps is > 0 ? row.Reps : null);
    }

    private static string Kg(double kg) => $"{kg.ToString("0.##", CultureInfo.InvariantCulture)} kg";
}