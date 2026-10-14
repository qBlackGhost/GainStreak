using System.Globalization;

namespace GymApp.Core;

/// <summary>Conversions for display. The database always holds kg and cm.</summary>
public static class Units
{
    public const double KgPerLb = 0.45359237;
    public const double CmPerInch = 2.54;

    public static double KgToLb(double kg) => kg / KgPerLb;
    public static double LbToKg(double lb) => lb * KgPerLb;

    public static double FeetInchesToCm(int feet, double inches) => (feet * 12 + inches) * CmPerInch;

    /// <summary>Rounded to whole inches, so 5 ft 11.6 in shows as 6 ft 0 in rather than 5 ft 12 in.</summary>
    public static (int Feet, int Inches) CmToFeetInches(double cm)
    {
        var totalInches = (int)Math.Round(cm / CmPerInch, MidpointRounding.AwayFromZero);
        return (totalInches / 12, totalInches % 12);
    }

    public static string FormatWeight(double kg, UnitSystem units) =>
        units == UnitSystem.Metric
            ? $"{NumberParsing.FormatKg(Math.Round(kg, 1))} kg"
            : $"{KgToLb(kg).ToString("0.#", CultureInfo.InvariantCulture)} lb";

    public static string FormatHeight(double cm, UnitSystem units)
    {
        if (units == UnitSystem.Metric) return $"{Math.Round(cm).ToString("0", CultureInfo.InvariantCulture)} cm";
        var (ft, inch) = CmToFeetInches(cm);
        return $"{ft} ft {inch} in";
    }

    /// <summary>The number shown in a weight input box, in the user's unit.</summary>
    public static string WeightInputText(double kg, UnitSystem units) =>
        units == UnitSystem.Metric
            ? NumberParsing.FormatKg(Math.Round(kg, 1))
            : KgToLb(kg).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>A positive decimal, with a point or a comma (same rules as weights).</summary>
    public static bool TryParseNumber(string? text, out double value) =>
        NumberParsing.TryParseKg(text, out value);

    /// <summary>A whole number, e.g. kcal. Spaces and thousands separators are ignored ("2,200", "2 200").</summary>
    public static bool TryParseWholeNumber(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var digits = text.Trim().Replace(",", "").Replace(".", "").Replace(" ", "");
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
