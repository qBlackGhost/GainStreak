using System.Globalization;

namespace GymApp.Core;

public static class NumberParsing
{
    /// <summary>Weight in kg: digits with an optional decimal point or comma. 0 is fine (bodyweight).</summary>
    public static bool TryParseKg(string? text, out double kg)
    {
        kg = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture, out kg)
               && double.IsFinite(kg);
    }

    /// <summary>Reps: a whole number of at least 1.</summary>
    public static bool TryParseReps(string? text, out int reps)
    {
        reps = 0;
        return !string.IsNullOrWhiteSpace(text)
               && int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out reps)
               && reps > 0;
    }

    public static string FormatKg(double kg) => kg.ToString("0.##", CultureInfo.InvariantCulture);
}