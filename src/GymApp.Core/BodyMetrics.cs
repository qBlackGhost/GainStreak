namespace GymApp.Core;

public enum Sex { Male, Female }

public enum UnitSystem { Metric, Imperial }

/// <summary>How active the user is outside the gym, picked from three step bands.
/// Stored as its name in the database, so don't rename the members.</summary>
public enum ActivityLevel { LessActive, Active, VeryActive }

/// <summary>Names and step ranges for the activity picker.</summary>
public static class ActivityLevels
{
    public static string DisplayName(ActivityLevel level) => level switch
    {
        ActivityLevel.LessActive => "Less Active",
        ActivityLevel.Active => "Active",
        ActivityLevel.VeryActive => "Very Active",
        _ => level.ToString()
    };

    public static string StepRange(ActivityLevel level) => level switch
    {
        ActivityLevel.LessActive => "under 7,500 steps / day",
        ActivityLevel.Active => "7,500 - 12,500 steps / day",
        ActivityLevel.VeryActive => "over 12,500 steps / day",
        _ => ""
    };
}

/// <summary>Sane ranges for body data. Everything is stored metric.</summary>
public static class BodyLimits
{
    public const double MinHeightCm = 100;
    public const double MaxHeightCm = 250;
    public const double MinWeightKg = 30;
    public const double MaxWeightKg = 350;
    public const int MinAge = 13;
    public const int MaxAge = 100;

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

    /// <summary>Lightly / moderately / very active factors. No 1.2 "sedentary" band:
    /// everyone using the app also trains.</summary>
    public static double ActivityFactor(ActivityLevel level) => level switch
    {
        ActivityLevel.LessActive => 1.375,
        ActivityLevel.Active => 1.55,
        ActivityLevel.VeryActive => 1.725,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    /// <summary>Maintenance calories (TDEE), rounded to the nearest 10.</summary>
    public static int Maintenance(Sex sex, double weightKg, double heightCm, int age, ActivityLevel level) =>
        RoundTo10(Bmr(sex, weightKg, heightCm, age) * ActivityFactor(level));

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
}
