using System;
using System.Collections.Generic;
using System.Linq;

namespace GymApp.Core;

/// <summary>One vacation as the rules see it. Frozen from <see cref="Start"/> up to the day before <see cref="Return"/>,
/// or up to <see cref="EndedEarlyOn"/> when the user came back early.</summary>
public record VacationPeriod(DateOnly Start, DateOnly Return, DateOnly? EndedEarlyOn = null);

/// <summary>Why a vacation can't start now.</summary>
public enum VacationRefusal
{
    None,
    AlreadyOnVacation,  // today is already frozen
    Cooldown,           // one started less than 60 days ago
    SettleMissedDay     // yesterday's miss needs a choice, or a day is owed and today isn't logged
}

/// <param name="AvailableFrom">With <see cref="VacationRefusal.Cooldown"/>, the first day a vacation can start.</param>
public record VacationCheck(VacationRefusal Refusal, DateOnly? AvailableFrom = null)
{
    public bool Allowed => Refusal == VacationRefusal.None;
}

/// <summary>
/// Vacation mode (0.19.0, docs/PHASE_19_VACATION.md): the streak is frozen for up to 7 days, once every 60 days
/// (counted between start dates).
/// On a frozen day the streak neither grows nor breaks unless a verified workout is logged (see
/// <see cref="StreakCalculator"/> and <see cref="RestDayRule.Frozen"/>).
/// </summary>
public static class VacationRules
{
    public const int MaxDays = 7;
    public const int CooldownDays = 60;
    public const double AwayMarginKm = 40;

    /// <summary>Home looks for a trip at most this often (19c).</summary>
    public static readonly TimeSpan AwayCheckInterval = TimeSpan.FromHours(6);

    /// <summary>Whether Home may read the position again to look for a trip.</summary>
    public static bool IsAwayCheckDue(DateTime now, DateTime? lastCheck) =>
        lastCheck is not { } last || now - last >= AwayCheckInterval || now < last;

    /// <summary>The frozen days of one vacation: start up to the day before the return date, or up to the day it ended early.</summary>
    public static IEnumerable<DateOnly> FrozenDays(VacationPeriod v)
    {
        var last = v.EndedEarlyOn is { } e && e < v.Return ? e : v.Return.AddDays(-1);
        for (var d = v.Start; d <= last; d = d.AddDays(1)) yield return d;
    }

    /// <summary>The first normal day after a vacation: its return date, or the day after it ended early.</summary>
    public static DateOnly BackOn(VacationPeriod v) =>
        v.EndedEarlyOn is { } e && e < v.Return ? e.AddDays(1) : v.Return;

    /// <summary>Every frozen day of every vacation.</summary>
    public static IReadOnlySet<DateOnly> FrozenDays(IEnumerable<VacationPeriod> vacations) =>
        vacations.SelectMany(FrozenDays).ToHashSet();

    /// <summary>The vacation in progress, or the one starting tomorrow (started after today's workout): the one with
    /// a frozen day today or tomorrow. Null when there's none.</summary>
    public static VacationPeriod? Current(IEnumerable<VacationPeriod> vacations, DateOnly today) =>
        vacations.FirstOrDefault(v => FrozenDays(v).Any(d => d == today || d == today.AddDays(1)));

    /// <summary>The first frozen day of a vacation started today: today, or tomorrow when today's workout is already
    /// logged, so that workout still counts as it did (on a frozen day an unverified workout doesn't add).</summary>
    public static DateOnly StartDate(DateOnly today, bool loggedToday) => loggedToday ? today.AddDays(1) : today;

    /// <summary>The return dates the user can pick for a vacation starting on <paramref name="start"/>: the day after
    /// it to 7 days after it, so at most 7 days are frozen.</summary>
    public static (DateOnly First, DateOnly Last) ReturnDateRange(DateOnly start) => (start.AddDays(1), start.AddDays(MaxDays));

    /// <summary>Whether a vacation can start now. Any earlier vacation started less than 60 days ago blocks it, even
    /// one ended early. A missed day waiting on a choice, or an owed day with today not logged, must be settled first
    /// (a vacation can't wipe a miss that already happened).</summary>
    public static VacationCheck CanStart(IEnumerable<VacationPeriod> vacations, StreakStatus status, DateOnly today)
    {
        var list = vacations.ToList();
        if (Current(list, today) is not null) return new(VacationRefusal.AlreadyOnVacation);
        if (list.Count > 0 && list.Max(v => v.Start).AddDays(CooldownDays) is var next && next > today)
            return new(VacationRefusal.Cooldown, next);
        if (status is StreakStatus.NeedsChoice or StreakStatus.AtRisk) return new(VacationRefusal.SettleMissedDay);
        return new(VacationRefusal.None);
    }

    /// <summary>Whether <paramref name="returnDate"/> is one the user may pick for a vacation starting on <paramref name="start"/>.</summary>
    public static bool IsValidReturnDate(DateOnly returnDate, DateOnly start)
    {
        var (first, last) = ReturnDateRange(start);
        return returnDate >= first && returnDate <= last;
    }

    /// <summary>More than 40 km from the home city's box, measured to its nearest edge (0 inside it).</summary>
    public static bool IsAway(LocationFix fix, double south, double west, double north, double east) =>
        DistanceToBoxMeters(fix.Latitude, fix.Longitude, south, west, north, east) > AwayMarginKm * 1000;

    /// <summary>Distance from a point to the nearest point of a latitude/longitude box, 0 inside it.</summary>
    public static double DistanceToBoxMeters(double lat, double lon, double south, double west, double north, double east)
    {
        var nearestLat = Math.Clamp(lat, Math.Min(south, north), Math.Max(south, north));
        var nearestLon = Math.Clamp(lon, Math.Min(west, east), Math.Max(west, east));
        return GymProximity.DistanceMeters(lat, lon, nearestLat, nearestLon);
    }
}
