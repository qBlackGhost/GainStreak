using System.Globalization;

namespace GymApp.Core;

/// <summary>A weigh-in in Health: when, how much, and the sync id when GainStreak wrote it.</summary>
public record HealthWeight(DateTime At, double Kg, string? SyncId = null);

/// <summary>New weigh-ins since the last import, and where the next import starts (an opaque anchor).</summary>
public record HealthWeights(IReadOnlyList<HealthWeight> Samples, byte[]? Anchor);

/// <summary>What Health knows of the user's body. Sex only when Health says female or male.</summary>
public record HealthProfile(double? HeightCm, DateOnly? BirthDate, Sex? Sex);

/// <summary>Health values that differ from ones the user typed (20c): offered, never applied without asking.
/// Null fields aren't offered.</summary>
public record HealthProfileOffer(double? HeightCm, DateOnly? BirthDate, Sex? Sex)
{
    public bool IsEmpty => HeightCm is null && BirthDate is null && Sex is null;
}

/// <summary>
/// Phase 20c: the rules for reading from Apple Health. Reads never overwrite silently (decision 5): weigh-ins fill
/// only days without a typed one, profile values fill only empty fields and offer the rest, and steps only suggest
/// an activity level.
/// </summary>
public static class HealthImport
{
    /// <summary>How far back weigh-ins are imported, from when Health was turned on (iOS may share only 30 days).</summary>
    public const int WeighInDaysBack = 90;

    /// <summary>Days with steps needed for a suggestion, out of the last <see cref="StepDays"/>.</summary>
    public const int MinStepDays = 14;
    public const int StepDays = 30;

    /// <summary>
    /// The weigh-ins to import: one per day (the day's last), from <paramref name="earliest"/> to
    /// <paramref name="today"/>, never on a day with a weigh-in typed in the app (<paramref name="typedDays"/>), never
    /// one GainStreak wrote (any account: its sync id starts with <see cref="HealthPlan.Prefix"/>), and only weights
    /// the app accepts (<see cref="BodyLimits"/>).
    /// </summary>
    public static IReadOnlyList<(DateOnly Date, double Kg)> WeighInsToImport(IEnumerable<HealthWeight> samples,
        IReadOnlySet<DateOnly> typedDays, DateOnly earliest, DateOnly today) =>
        samples
            .Where(s => s.SyncId?.StartsWith(HealthPlan.Prefix, StringComparison.Ordinal) != true)
            .Where(s => s.Kg is >= BodyLimits.MinWeightKg and <= BodyLimits.MaxWeightKg)
            .Select(s => (Date: DateOnly.FromDateTime(s.At), s.At, s.Kg))
            .Where(s => s.Date >= earliest && s.Date <= today && !typedDays.Contains(s.Date))
            .GroupBy(s => s.Date)
            .Select(g => g.MaxBy(s => s.At))
            .OrderBy(s => s.Date)
            .Select(s => (s.Date, Math.Round(s.Kg, 2)))
            .ToList();

    /// <summary>The average of the days with steps, or null with fewer than <see cref="MinStepDays"/> of them (a new
    /// phone, or steps not shared).</summary>
    public static double? AverageSteps(IEnumerable<double> dailyTotals)
    {
        var days = dailyTotals.Where(d => d > 0).ToList();
        return days.Count < MinStepDays ? null : days.Average();
    }

    /// <summary>The activity band for an average of daily steps, as the picker names them
    /// (<see cref="ActivityLevels.StepRange"/>).</summary>
    public static ActivityLevel LevelForSteps(double averageSteps) =>
        averageSteps < 7500 ? ActivityLevel.LessActive
        : averageSteps <= 12500 ? ActivityLevel.Active
        : ActivityLevel.VeryActive;

    /// <summary>The level to suggest: the steps' level when it differs from the user's and from the last one they
    /// answered; otherwise null.</summary>
    public static ActivityLevel? StepsSuggestion(double? averageSteps, ActivityLevel? current, ActivityLevel? lastAnswered) =>
        averageSteps is { } avg && LevelForSteps(avg) is var level && level != current && level != lastAnswered ? level : null;

    /// <summary>
    /// Splits Health's profile into what fills empty fields (<c>Fill</c>) and what differs from a typed value
    /// (<c>Offer</c>). Heights within 1 cm count as the same. A value that's out of the app's range is ignored.
    /// </summary>
    public static (HealthProfile Fill, HealthProfileOffer Offer) Compare(HealthProfile health, double? heightCm,
        DateOnly? birthDate, Sex? sex, DateOnly today)
    {
        var h = health.HeightCm is { } hc && hc is >= BodyLimits.MinHeightCm and <= BodyLimits.MaxHeightCm ? Math.Round(hc, 1) : (double?)null;
        var b = health.BirthDate is { } bd && BodyMetrics.AgeOn(bd, today) is >= BodyLimits.MinAge and <= BodyLimits.MaxAge ? bd : (DateOnly?)null;
        var s = health.Sex;
        var fill = new HealthProfile(heightCm is null ? h : null, birthDate is null ? b : null, sex is null ? s : null);
        var offer = new HealthProfileOffer(
            heightCm is { } mine && h is { } theirs && Math.Abs(mine - theirs) >= 1 ? theirs : null,
            birthDate is { } myBirth && b is { } theirBirth && myBirth != theirBirth ? theirBirth : null,
            sex is { } mySex && s is { } theirSex && mySex != theirSex ? theirSex : null);
        return (fill, offer);
    }

    /// <summary>Names an offer, so "Keep mine" can be remembered for exactly these values.</summary>
    public static string OfferKey(HealthProfileOffer offer) => string.Join("|",
        offer.HeightCm?.ToString("0.0", CultureInfo.InvariantCulture) ?? "",
        offer.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        offer.Sex?.ToString() ?? "");
}
