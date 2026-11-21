using System.Globalization;

namespace GymApp.Core;

/// <summary>
/// The words and numbers the badge screens show (Phase 25): "61 t / 150 t", "Fire · 100 days", "1.3× / 1.5×".
/// Weights follow the user's units: Heavy Lifter shows metric tonnes, or US tons (2,000 lb) in imperial.
/// </summary>
public static class BadgeDisplay
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    private const double KgPerShortTon = 907.18474;

    /// <summary>What a badge counts, plural ("days", "workouts"); null for amounts with their own unit (weights, ×).</summary>
    public static string? Unit(string id) => id switch
    {
        "streak" or "fuel-streak" or "rest-day-never" or "we-go-jim" => "days",
        "perfect-week" or "pr-week" or "weigh-in-habit" or "never-skip-leg-day" or "top-of-the-board" => "weeks",
        "weekend-warrior" => "weekends",
        "split-finisher" => "cycles",
        "pr-breaker" => "PRs",
        "century" or "regular" => "workouts",
        "rep-machine" => "reps",
        "exercise-explorer" => "exercises",
        "cardio-minutes" => "min",
        "explorer" => "gyms",
        "first-of-month" => "firsts",
        "full-house" => "full plans",
        "completion" => "badges",
        "recruiter" => "friends",
        _ when id.StartsWith("monthly-", StringComparison.Ordinal) => "workouts",
        _ => null,
    };

    /// <summary>An amount of what <paramref name="id"/> counts, with its unit: "61 t", "1.3×", "940 min", "9 days".</summary>
    public static string Amount(string id, double value, bool imperial)
    {
        switch (id)
        {
            case "heavy-lifter":
                return imperial
                    ? $"{Number(value / KgPerShortTon)} tons"
                    : $"{Number(value / 1000)} t";
            case "bodyweight-club":
                return $"{value.ToString(value % 1 == 0 ? "0" : "0.0#", Ci)}×";
            default:
                var unit = Unit(id);
                var n = Number(value);
                return unit is null ? n : $"{n} {(n == "1" ? Singular(unit) : unit)}";
        }
    }

    /// <summary>Progress to the next tier or to the badge, "9 / 14 days"; null once there's nothing left to reach.</summary>
    public static string? Progress(BadgeDefinition badge, BadgeProgress progress, bool imperial)
    {
        if (progress.Target is not { } target) return null;
        var full = Amount(badge.Id, target, imperial);
        var value = Amount(badge.Id, Math.Min(progress.Value, target), imperial);
        // "9 / 14 days" reads better than "9 days / 14 days"; weights and × keep their units on both sides
        if (Unit(badge.Id) is { } unit)
            foreach (var word in new[] { unit, Singular(unit) })
                if (value.EndsWith(" " + word, StringComparison.Ordinal)) { value = value[..^(word.Length + 1)]; break; }
        return $"{value} / {full}";
    }

    /// <summary>How far along, 0 to 1, for the progress bar.</summary>
    public static double Ratio(BadgeProgress progress) =>
        progress.Target is { } target && target > 0 ? Math.Clamp(progress.Value / target, 0, 1) : 1;

    /// <summary>The tier's threshold: "100 days", "6 t", "1.5×".</summary>
    public static string Threshold(BadgeDefinition badge, int tier, bool imperial) =>
        Amount(badge.Id, badge.Tiers[tier - 1], imperial);

    /// <summary>"Fire · 100 days", "Elephant", "Earned", "Locked".</summary>
    public static string Status(BadgeDefinition badge, bool earned, int tier, bool imperial)
    {
        if (!earned) return "Locked";
        if (!badge.IsTiered || tier < 1) return "Earned";
        var name = BadgeCatalog.TierName(badge, tier)!;
        return badge.TierNames is not null ? name : $"{name} · {Threshold(badge, tier, imperial)}";
    }

    /// <summary>"week" for "weeks", "PR" for "PRs"; units that aren't plurals ("min") stay.</summary>
    private static string Singular(string unit) =>
        unit is "min" ? unit : unit.EndsWith('s') ? unit[..^1] : unit;

    private static string Number(double v) =>
        v >= 10 || v % 1 == 0 ? Math.Round(v).ToString("N0", Ci) : v.ToString("0.#", Ci);
}

/// <summary>The featured badge as it travels (Phase 25): <c>"&lt;id&gt;:&lt;tier&gt;"</c>, e.g. <c>"streak:3"</c>.</summary>
public static class FeaturedBadge
{
    public static string Format(string id, int tier) => $"{id}:{tier.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The id and tier, or null for null or anything malformed.</summary>
    public static (string Id, int Tier)? Parse(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var i = value.LastIndexOf(':');
        return i > 0 && int.TryParse(value[(i + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var tier) && tier is >= 0 and <= BadgeCatalog.MaxTier
            ? (value[..i], tier)
            : null;
    }
}
