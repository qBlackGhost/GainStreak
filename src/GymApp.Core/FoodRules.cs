using System;

namespace GymApp.Core;

/// <summary>Where a food came from (Phase 26): the bundled USDA catalog, Open Food Facts, or the user's own.</summary>
public enum FoodSource { Usda, OpenFoodFacts, Mine }

public enum Meal { Breakfast, Lunch, Dinner, Snacks }

public static class MealDefaults
{
    /// <summary>The meal the Add food sheet starts on: before 11:00 Breakfast, before 16:00 Lunch, before 21:00 Dinner,
    /// else Snacks.</summary>
    public static Meal ForTime(TimeOnly time) =>
        time < new TimeOnly(11, 0) ? Meal.Breakfast
        : time < new TimeOnly(16, 0) ? Meal.Lunch
        : time < new TimeOnly(21, 0) ? Meal.Dinner
        : Meal.Snacks;

    public static string DisplayName(Meal meal) => meal.ToString();
}

/// <summary>Sanity rules for food numbers (Phase 26), the same for USDA, Open Food Facts and the user's own foods, and
/// the arithmetic for amounts. Nutrients are per 100 g (100 ml for liquids).</summary>
public static class FoodRules
{
    public const double MaxKcalPer100 = 900;
    public const double MaxMacroPer100 = 100;
    /// <summary>Protein + carbs + fat may exceed 100 g by this much (rounding in the source data).</summary>
    public const double MacroSumSlack = 2;
    public const double MinServingGrams = 1;
    public const double MaxServingGrams = 2000;
    /// <summary>The most one entry can be, in g or ml.</summary>
    public const double MaxAmountGrams = 5000;
    public const double KjPerKcal = 4.184;
    public const int MaxNameLength = 200;
    public const int MaxBrandLength = 120;
    public const int MaxLabelLength = 80;

    public const string LooksWrong = "This product's numbers look wrong. Check them before logging.";
    public const string NoEnergy = "This product has no calorie value. Add it before logging.";

    public static double KjToKcal(double kj) => kj / KjPerKcal;

    /// <summary>Null when the numbers are usable. A missing energy value is <see cref="NoEnergy"/>; anything out of range,
    /// or macros adding up to more than the food weighs, is <see cref="LooksWrong"/>. Missing macros are fine.</summary>
    public static string? CheckPer100(double? kcal, double? protein, double? carbs, double? fat)
    {
        if (kcal is null) return NoEnergy;
        if (!IsFinite(kcal.Value) || kcal < 0 || kcal > MaxKcalPer100) return LooksWrong;
        double sum = 0;
        foreach (var m in new[] { protein, carbs, fat })
        {
            if (m is not { } v) continue;
            if (!IsFinite(v) || v < 0 || v > MaxMacroPer100) return LooksWrong;
            sum += v;
        }
        return sum > MaxMacroPer100 + MacroSumSlack ? LooksWrong : null;
    }

    /// <summary>Null when the serving is missing (that's allowed) or between 1 g and 2,000 g.</summary>
    public static string? CheckServing(double? grams) =>
        grams is { } g && (!IsFinite(g) || g < MinServingGrams || g > MaxServingGrams) ? LooksWrong : null;

    /// <summary>The check for the New food form: specific messages, per 100 g or ml.</summary>
    public static string? CheckInput(string? name, string? brand, double? kcal, double? protein, double? carbs, double? fat,
        double? servingGrams, string? servingName)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Give the food a name.";
        if (name.Trim().Length > MaxNameLength) return $"The name can be at most {MaxNameLength} characters.";
        if (brand is { } b && b.Trim().Length > MaxBrandLength) return $"The brand can be at most {MaxBrandLength} characters.";
        if (kcal is null) return "Enter the calories.";
        if (!IsFinite(kcal.Value) || kcal < 0 || kcal > MaxKcalPer100) return $"Calories per 100 g must be between 0 and {MaxKcalPer100:0}.";
        foreach (var (label, v) in new[] { ("Protein", protein), ("Carbs", carbs), ("Fat", fat) })
            if (v is { } x && (!IsFinite(x) || x < 0 || x > MaxMacroPer100))
                return $"{label} per 100 g must be between 0 and {MaxMacroPer100:0} g.";
        if (((protein ?? 0) + (carbs ?? 0) + (fat ?? 0)) > MaxMacroPer100 + MacroSumSlack)
            return "Protein, carbs and fat can't add up to more than 100 g in 100 g.";
        if (servingGrams is { } g && (!IsFinite(g) || g < MinServingGrams || g > MaxServingGrams))
            return $"A serving must be between {MinServingGrams:0} and {MaxServingGrams:#,0} g.";
        if (servingName is { Length: > 80 }) return "The serving name is too long.";
        return null;
    }

    /// <summary>Null when the amount is between 0 (excluded) and <see cref="MaxAmountGrams"/>.</summary>
    public static string? CheckAmount(double grams) =>
        !IsFinite(grams) || grams <= 0 || grams > MaxAmountGrams
            ? $"The amount must be more than 0 and at most {MaxAmountGrams:#,0}."
            : null;

    /// <summary>Per-serving numbers to per 100 g.</summary>
    public static double To100(double perServing, double servingGrams) => perServing * 100 / servingGrams;

    /// <summary>A nutrient for an amount, rounded to one decimal.</summary>
    public static double Scale(double per100, double grams) => Math.Round(per100 * grams / 100, 1, MidpointRounding.AwayFromZero);

    public static int KcalFor(double kcalPer100, double grams) =>
        (int)Math.Round(kcalPer100 * grams / 100, MidpointRounding.AwayFromZero);

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
