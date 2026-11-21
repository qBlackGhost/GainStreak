using System.Globalization;

namespace GymApp.Core;

/// <summary>
/// Monthly Challenge (Phase 25): one challenge per month, built in ("N workouts this month"). Each month is its own
/// one-time badge, <c>monthly-YYYY-MM</c>, earnable only with that month's workouts. Months before
/// <see cref="FirstMonth"/> have no challenge.
/// </summary>
public static class MonthlyChallenges
{
    /// <summary>The first month with a challenge: the month badges ship in.</summary>
    public static readonly DateOnly FirstMonth = new(2026, 12, 1);

    // Workouts to log, January first. Short months and holiday months ask for fewer.
    private static readonly int[] Targets = [16, 12, 14, 14, 15, 13, 12, 12, 15, 15, 16, 12];

    private const string Prefix = "monthly-";

    /// <summary>The badge id for the month containing <paramref name="d"/>.</summary>
    public static string IdFor(DateOnly d) => $"{Prefix}{d.Year:D4}-{d.Month:D2}";

    /// <summary>How many workouts the month containing <paramref name="d"/> asks for.</summary>
    public static int Target(DateOnly d) => Targets[d.Month - 1];

    /// <summary>The first day of every month with a challenge, from <see cref="FirstMonth"/> to the month of
    /// <paramref name="today"/>.</summary>
    public static IEnumerable<DateOnly> MonthsUntil(DateOnly today)
    {
        var last = new DateOnly(today.Year, today.Month, 1);
        for (var m = FirstMonth; m <= last; m = m.AddMonths(1)) yield return m;
    }

    /// <summary>The month's badge, or null when <paramref name="id"/> isn't a monthly id with a challenge.</summary>
    public static BadgeDefinition? Definition(string id)
    {
        if (!id.StartsWith(Prefix, StringComparison.Ordinal)
            || !DateOnly.TryParseExact(id[Prefix.Length..] + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var month)
            || month < FirstMonth)
            return null;
        var name = month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        return new BadgeDefinition(id, "Monthly Challenge", BadgeGroup.Social,
            $"{name}: log {Target(month)} workouts this month.", [], null,
            Text: month.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant(), Material: BadgeMaterial.Limited);
    }
}
