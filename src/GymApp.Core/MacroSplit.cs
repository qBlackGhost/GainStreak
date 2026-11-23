using System;
using System.Linq;

namespace GymApp.Core;

/// <summary>How a day's calories divide between protein, carbs and fat (Phase 27). Protein and carbs are 4 kcal per gram, fat 9.
/// Percentages are whole numbers that add up to 100 (largest remainder), so the ring and the legend always agree.</summary>
public record MacroSplit(
    double ProteinGrams, double CarbsGrams, double FatGrams,
    double ProteinKcal, double CarbsKcal, double FatKcal,
    int ProteinPercent, int CarbsPercent, int FatPercent)
{
    public double TotalKcal => ProteinKcal + CarbsKcal + FatKcal;

    /// <summary>The split of a day's macros; null when they add up to no calories (nothing to split).</summary>
    public static MacroSplit? From(double? protein, double? carbs, double? fat)
    {
        double p = Clean(protein), c = Clean(carbs), f = Clean(fat);
        double pk = p * FoodRules.KcalPerGramProtein, ck = c * FoodRules.KcalPerGramCarbs, fk = f * FoodRules.KcalPerGramFat;
        var total = pk + ck + fk;
        if (total <= 0) return null;
        var shares = Percentages(pk, ck, fk);
        return new MacroSplit(p, c, f, pk, ck, fk, shares[0], shares[1], shares[2]);
    }

    /// <summary>Whole-number shares of the three values, adding up to 100. All zero gives 0, 0, 0. The leftover points go to
    /// the largest fractions first (ties: protein, carbs, fat).</summary>
    public static int[] Percentages(params double[] values)
    {
        var total = values.Sum();
        var result = new int[values.Length];
        if (total <= 0) return result;
        var exact = values.Select(v => v * 100 / total).ToArray();
        for (var i = 0; i < exact.Length; i++) result[i] = (int)Math.Floor(exact[i] + 1e-9);
        var left = 100 - result.Sum();
        foreach (var i in Enumerable.Range(0, exact.Length).OrderByDescending(i => exact[i] - result[i]).ThenBy(i => i).Take(Math.Max(left, 0)))
            result[i]++;
        return result;
    }

    /// <summary>How full a macro's bar is, 0 to 1. With no target it is the macro's share of the day's calories; with one it is
    /// grams ÷ target, capped at 1. <paramref name="over"/> is true past the target.</summary>
    public static double BarFill(double grams, int? targetGrams, double shareOfCalories, out bool over)
    {
        over = false;
        if (targetGrams is > 0 and var t)
        {
            over = grams > t;
            return Math.Clamp(grams / t, 0, 1);
        }
        return Math.Clamp(shareOfCalories, 0, 1);
    }

    /// <summary>This macro's exact share of the day's calories, 0 to 1 (not the rounded percentage).</summary>
    public double ShareOf(Macro macro) => TotalKcal <= 0 ? 0 : macro switch
    {
        Macro.Protein => ProteinKcal / TotalKcal,
        Macro.Carbs => CarbsKcal / TotalKcal,
        _ => FatKcal / TotalKcal
    };

    /// <summary>"Protein 26 percent, carbs 43 percent, fat 31 percent", for screen readers.</summary>
    public string Describe() => $"Protein {ProteinPercent} percent, carbs {CarbsPercent} percent, fat {FatPercent} percent";

    public int PercentOf(Macro macro) => macro switch { Macro.Protein => ProteinPercent, Macro.Carbs => CarbsPercent, _ => FatPercent };

    private static double Clean(double? v) => v is { } x && !double.IsNaN(x) && !double.IsInfinity(x) && x > 0 ? x : 0;
}

public enum Macro { Protein, Carbs, Fat }
