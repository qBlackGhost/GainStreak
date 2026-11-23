using System;
using System.Collections.Generic;
using System.Linq;

namespace GymApp.Core;

/// <summary>Whether the calorie goal and macro targets follow the recommendation or the user's own grams (Phase 27). Stored
/// as its name in the database, so don't rename the members. Null on the user means neither was chosen yet (the goal stays as
/// it was before Phase 27).</summary>
public enum MacroMode { Recommended, Custom }

/// <summary>A recommended day: grams of each macro, the grams per kg they came from, and the calories they add up to.</summary>
public record MacroRecommendation(
    int Protein, int Carbs, int Fat, int Kcal,
    double ProteinPerKg, double CarbsPerKg, double FatPerKg,
    double WeightKg, double ProteinWeightKg, bool RaisedToMinimum);

/// <summary>
/// The recommended macros (Phase 27), set first and in grams per kg of body weight; the calorie goal is what they add up to.
/// Protein follows the ISSN stands and Helms et al. (more in a deficit to keep muscle), the same for men and women. Fat stays at
/// or above the 0.5 to 1 g/kg sports range, a little higher for women. Carbs sit low in a cut and high in a surplus within the
/// 3 to 7 g/kg range for lifters, a little lower for women (contest-prep intakes), and move 0.5 g/kg per activity step.
/// Protein uses the lower of the weight and the weight at BMI 27, so a heavier body isn't given protein for its fat.
/// </summary>
public static class MacroPlan
{
    /// <summary>Protein is worked out from at most the weight at this BMI.</summary>
    public const double ProteinBmiCap = 27;
    /// <summary>Carbs per kg move this much per activity step away from Active.</summary>
    public const double CarbsPerActivityStep = 0.5;
    /// <summary>The weight the recommendation follows is the average of this many days of weigh-ins.</summary>
    public const int TrendDays = 14;
    /// <summary>The recommendation moves only when the trend weight is this far from the weight it was worked out from.</summary>
    public const double TrendThresholdKg = 1.0;

    /// <summary>Grams per kg of body weight for protein, fat and carbs (carbs for the Active level).</summary>
    public static (double Protein, double Fat, double Carbs) PerKg(GoalType goal, Sex sex) => (goal, sex) switch
    {
        (GoalType.Lose, Sex.Male) => (2.2, 0.7, 2.5),
        (GoalType.Lose, _) => (2.2, 0.9, 2.0),
        (GoalType.Gain, Sex.Male) => (1.8, 1.0, 5.0),
        (GoalType.Gain, _) => (1.8, 1.1, 4.5),
        (_, Sex.Male) => (1.8, 0.9, 4.0),
        _ => (1.8, 1.0, 3.5),
    };

    public static double ActivityCarbStep(ActivityLevel? level) => level switch
    {
        ActivityLevel.LessActive => -CarbsPerActivityStep,
        ActivityLevel.VeryActive => CarbsPerActivityStep,
        _ => 0
    };

    /// <summary>The weight protein is worked out from: the weight, or the weight at BMI 27 for the height when that is lower.</summary>
    public static double ProteinWeight(double weightKg, double? heightCm)
    {
        if (heightCm is not { } h || h <= 0) return weightKg;
        var m = h / 100;
        return Math.Min(weightKg, ProteinBmiCap * m * m);
    }

    /// <summary>The recommended grams and the calories they add up to. Under <paramref name="safeMinimum"/> the carbs go up until
    /// the calories reach it.</summary>
    public static MacroRecommendation Recommend(GoalType goal, Sex sex, double weightKg, double? heightCm, ActivityLevel? activity,
        int? safeMinimum = null)
    {
        var (p, f, c) = PerKg(goal, sex);
        c += ActivityCarbStep(activity);
        var proteinWeight = ProteinWeight(weightKg, heightCm);

        var protein = Grams(p * proteinWeight);
        var fat = Grams(f * weightKg);
        var carbs = Grams(c * weightKg);
        var kcal = FoodRules.KcalFromMacros(protein, carbs, fat);
        var raised = false;
        if (safeMinimum is { } min && kcal < min)
        {
            carbs += (int)Math.Ceiling((min - kcal) / (double)FoodRules.KcalPerGramCarbs);
            kcal = FoodRules.KcalFromMacros(protein, carbs, fat);
            raised = true;
        }
        return new(protein, carbs, fat, kcal, p, c, f, weightKg, Math.Round(proteinWeight, 1), raised);
    }

    /// <summary>The weight to plan with: the average of the weigh-ins in the last <see cref="TrendDays"/> days up to
    /// <paramref name="today"/>, else the latest weigh-in, else null. A day's fluctuation (water, salt, carbs) barely moves it.</summary>
    public static double? TrendWeight(IEnumerable<(DateOnly Date, double Kg)> weighIns, DateOnly today)
    {
        var all = weighIns.Where(w => w.Date <= today).OrderByDescending(w => w.Date).ToList();
        if (all.Count == 0) return null;
        var from = today.AddDays(-(TrendDays - 1));
        var recent = all.Where(w => w.Date >= from).ToList();
        return Math.Round(recent.Count > 0 ? recent.Average(w => w.Kg) : all[0].Kg, 1);
    }

    /// <summary>True when the trend has moved far enough from the weight the targets were worked out from to update them.</summary>
    public static bool TrendMoved(double? basisKg, double trendKg) =>
        basisKg is not { } b || Math.Abs(trendKg - b) >= TrendThresholdKg - 1e-9;

    private static int Grams(double g) => (int)Math.Round(g, MidpointRounding.AwayFromZero);
}

/// <summary>Checks for the custom macros on the goal sheet: all three are needed, since the goal is worked out from them.</summary>
public static class MacroPlanChecks
{
    public const int MaxGrams = 1000;

    public static string? Grams(string label, int? grams) => grams switch
    {
        null => $"Enter your {label.ToLowerInvariant()} in grams.",
        < 0 or > MaxGrams => $"{label} must be between 0 and {CalorieGoals.K(MaxGrams)} g.",
        _ => null
    };
}
