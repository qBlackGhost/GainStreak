using System.Globalization;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// A whole number of at least 0, e.g. kcal or feet. Thousands may be grouped with ',', '.' or a space, but only
    /// between groups of exactly three digits ("2,100", "2.100", "2 100"); "210.5" or "2,1" are rejected rather
    /// than read as 2105 or 21.
    /// </summary>
    public static bool TryParseWhole(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (!WholePattern.IsMatch(t)) return false;
        return int.TryParse(t.Replace(",", "").Replace(".", "").Replace(" ", ""), NumberStyles.None,
            CultureInfo.InvariantCulture, out value);
    }

    // Plain digits, or 1-3 digits then groups of 3 with one separator used throughout
    private static readonly Regex WholePattern = new(@"^(\d+|\d{1,3}([,. ])\d{3}(\2\d{3})*)$", RegexOptions.CultureInvariant);

    public static string FormatKg(double kg) => kg.ToString("0.##", CultureInfo.InvariantCulture);
}