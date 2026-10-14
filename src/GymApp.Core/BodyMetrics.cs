using System.Globalization;

namespace GymApp.Core;

public enum Sex { Male, Female }

public enum UnitSystem { Metric, Imperial }

/// <summary>Sane ranges for body data. Everything is stored metric.</summary>
public static class BodyLimits
{
    public const double MinHeightCm = 100;
    public const double MaxHeightCm = 250;
    public const double MinWeightKg = 30;
    public const double MaxWeightKg = 350;
    public const int MinAge = 13;
    public const int MaxAge = 100;
    public const int MaxSteps = 60_000;

    /// <summary>Automatic calorie estimates are only shown from this age.</summary>
    public const int AdultAge = 18;
}

/// <summary>BMR, activity factor and maintenance calories (TDEE). All of these are estimates.</summary>
public static class BodyMetrics
{
    /// <summary>Full years on <paramref name="today"/>.</summary>
    public static int AgeOn(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age)) age--;
        return age;
    }

    /// <summary>Mifflin-St Jeor: 10 x kg + 6.25 x cm - 5 x age, then +5 for men or -161 for women.</summary>
    public static double Bmr(Sex sex, double weightKg, double heightCm, int age) =>
        10 * weightKg + 6.25 * heightCm - 5 * age + (sex == Sex.Male ? 5 : -161);

    /// <summary>Starting step bands (tune later).</summary>
    public static double ActivityFactor(int stepsPerDay) => stepsPerDay switch
    {
        < 5_000 => 1.2,
        < 7_500 => 1.375,
        < 10_000 => 1.55,
        < 12_500 => 1.725,
        _ => 1.9
    };

    /// <summary>Maintenance calories (TDEE), rounded to the nearest 10.</summary>
    public static int Maintenance(Sex sex, double weightKg, double heightCm, int age, int stepsPerDay) =>
        RoundTo10(Bmr(sex, weightKg, heightCm, age) * ActivityFactor(stepsPerDay));

    public static int RoundTo10(double kcal) =>
        (int)(Math.Round(kcal / 10, MidpointRounding.AwayFromZero) * 10);

    /// <summary>Error text, or null when the value is in range.</summary>
    public static string? CheckHeight(double cm) =>
        cm is < BodyLimits.MinHeightCm or > BodyLimits.MaxHeightCm
            ? $"Height must be between {BodyLimits.MinHeightCm:0} and {BodyLimits.MaxHeightCm:0} cm."
            : null;

    public static string? CheckWeight(double kg) =>
        kg is < BodyLimits.MinWeightKg or > BodyLimits.MaxWeightKg
            ? $"Weight must be between {BodyLimits.MinWeightKg:0} and {BodyLimits.MaxWeightKg:0} kg."
            : null;

    public static string? CheckBirthDate(DateOnly birthDate, DateOnly today)
    {
        if (birthDate > today) return "Birth date can't be in the future.";
        var age = AgeOn(birthDate, today);
        return age is < BodyLimits.MinAge or > BodyLimits.MaxAge
            ? $"Age must be between {BodyLimits.MinAge} and {BodyLimits.MaxAge}."
            : null;
    }

    public static string? CheckSteps(int steps) =>
        steps is < 0 or > BodyLimits.MaxSteps
            ? $"Daily steps must be between 0 and {BodyLimits.MaxSteps.ToString("N0", CultureInfo.InvariantCulture)}."
            : null;
}
