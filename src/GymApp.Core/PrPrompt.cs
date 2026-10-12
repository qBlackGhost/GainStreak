using System.Globalization;

namespace GymApp.Core;

public enum PrStatus { Info, Below, Tie, NewPr }

public record PrPrompt(string Text, PrStatus Status);

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

    private static string Kg(double kg) => $"{kg.ToString("0.##", CultureInfo.InvariantCulture)} kg";
}