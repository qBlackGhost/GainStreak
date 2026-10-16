using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GymApp.Core;

/// <summary>The gym on one workout log. Key identifies the real place: OSM gyms have a row per city
/// (city boxes overlap), so the key is the OSM type and id, and the same gym counts once whichever city it was
/// picked in. Custom gyms use their own id.</summary>
public record VisitedGym(string Key, string Name, string? Address, bool IsActive, bool IsCustom)
{
    public static string OsmKey(OsmElementType type, long id) => $"osm:{type}:{id}";
    public static string CustomKey(int gymId) => $"custom:{gymId}";
}

/// <summary>One workout log in the month; Gym is null when no gym was logged that day.</summary>
public record MonthLog(DateOnly Date, VisitedGym? Gym);

/// <summary>How often one gym was visited. IsActive false = it closed or the user deleted it; still counted.</summary>
public record GymVisits(string Key, string Name, string? Address, bool IsActive, bool IsCustom, int Visits);

public record MonthSummary(int Year, int Month, IReadOnlyList<GymVisits> Gyms, int DistinctGyms, int TotalWorkouts, int NoGymLogged)
{
    public bool IsEmpty => TotalWorkouts == 0;
}

/// <summary>The monthly "gyms visited" summary. Always computed from the logs, never stored.</summary>
public static class GymSummaries
{
    private static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);

    /// <summary>First and last day of a calendar month (local dates, as logs are stored).</summary>
    public static (DateOnly First, DateOnly Last) MonthRange(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    /// <summary>Months after the current one have no workouts yet, so the summary never goes there.</summary>
    public static bool IsFuture(int year, int month, DateOnly today) => (year, month).CompareTo((today.Year, today.Month)) > 0;

    public static (int Year, int Month) Shift(int year, int month, int by)
    {
        var d = new DateOnly(year, month, 1).AddMonths(by);
        return (d.Year, d.Month);
    }

    /// <summary>Visits per gym (most first, then by name), distinct gyms, total workouts and days with no gym.
    /// Logs outside the month are ignored. Each gym shows the name and address from its latest visit.</summary>
    public static MonthSummary Build(int year, int month, IEnumerable<MonthLog> logs)
    {
        var (first, last) = MonthRange(year, month);
        var inMonth = logs.Where(l => l.Date >= first && l.Date <= last).ToList();

        var gyms = inMonth
            .Where(l => l.Gym is not null)
            .GroupBy(l => l.Gym!.Key)
            .Select(g =>
            {
                var latest = g.OrderByDescending(l => l.Date).First().Gym!;
                // Active if any of its rows is still listed (an OSM gym can be closed in one city's list only)
                var active = g.Any(l => l.Gym!.IsActive);
                return new GymVisits(g.Key, latest.Name, latest.Address, active, latest.IsCustom, g.Count());
            })
            .OrderByDescending(v => v.Visits)
            .ThenBy(v => v.Name, NameOrder)
            .ThenBy(v => v.Key, StringComparer.Ordinal)
            .ToList();

        return new MonthSummary(year, month, gyms, gyms.Count, inMonth.Count, inMonth.Count(l => l.Gym is null));
    }

    public static string MonthTitle(int year, int month) =>
        new DateOnly(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
}
