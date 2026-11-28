using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>Where vacation mode stands for the user today: the vacation in progress, if any, and whether a new one
/// can start (with why not).</summary>
public record VacationState(VacationPeriod? Current, VacationCheck Check);

/// <summary>Where Home should look for a trip (19c): the home city's name and box.</summary>
public record AwayCheckTarget(string CityName, double South, double West, double North, double East);

/// <param name="Message">Why it was refused, for the UI.</param>
public record VacationResult(bool Success, string? Message = null, StreakResult? Streak = null, PointsSync? Points = null);

/// <summary>
/// Vacation mode (0.19.0, docs/PHASE_19_VACATION.md): freezes the streak from today up to the day before a return date
/// at most 7 days away, once every 60 days. The rules are <see cref="VacationRules"/>; the calculator reads the frozen
/// days through <see cref="StreakService.LoadRestRuleAsync"/>. Starting or ending one recalculates the streak and
/// points, which marks the stats for upload.
/// </summary>
public class VacationService(IDbContextFactory<AppDbContext> dbFactory, StreakService streaks, PointsService points)
{
    public async Task<VacationState> GetStateAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        var streak = await streaks.GetAsync(userId, day);
        await using var db = await dbFactory.CreateDbContextAsync();
        var vacations = await StreakService.LoadVacationsAsync(db, userId);
        return new VacationState(VacationRules.Current(vacations, day), VacationRules.CanStart(vacations, streak.Status, day));
    }

    /// <summary>The vacation in progress or starting tomorrow, or null.</summary>
    public async Task<VacationPeriod?> GetCurrentAsync(int userId, DateOnly? today = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return VacationRules.Current(await StreakService.LoadVacationsAsync(db, userId), today ?? AppClock.Today);
    }

    public async Task<VacationCheck> CanStartAsync(int userId, DateOnly? today = null) =>
        (await GetStateAsync(userId, today)).Check;

    /// <summary>The first frozen day of a vacation started now (see <see cref="VacationRules.StartDate"/>); the
    /// sheet's return dates are <see cref="VacationRules.ReturnDateRange"/> of it.</summary>
    public async Task<DateOnly> StartDateAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        return VacationRules.StartDate(day, await db.WorkoutLogs.AnyAsync(w => w.UserId == userId && w.Date == day));
    }

    /// <summary>Starts a vacation (today, or tomorrow when today is already logged), back on <paramref name="returnDate"/>.
    /// Refused (with a message, never an exception) when <see cref="VacationRules.CanStart"/> says no or the return
    /// date is out of range.</summary>
    public async Task<VacationResult> StartAsync(int userId, DateOnly returnDate, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        var start = await StartDateAsync(userId, day);
        if (!VacationRules.IsValidReturnDate(returnDate, start))
            return new VacationResult(false, $"Pick a return date within {VacationRules.MaxDays} days.");

        var check = await CanStartAsync(userId, day);
        if (!check.Allowed) return new VacationResult(false, RefusalText(check));

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Vacations.Add(new Vacation { UserId = userId, StartDate = start, ReturnDate = returnDate });
            await db.SaveChangesAsync();
        }
        return await RecalculateAsync(userId, day);
    }

    /// <summary>"I'm back": today stays frozen, tomorrow is a normal day again, and the 60 days still count from the
    /// start. A vacation that hasn't started yet (it starts tomorrow) is cancelled instead and uses nothing up.</summary>
    public async Task<VacationResult> EndEarlyAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var rows = await db.Vacations.Where(v => v.UserId == userId && v.ReturnDate > day).ToListAsync();
            var current = rows.FirstOrDefault(v => v.EndedEarlyOn is null || v.EndedEarlyOn >= day);
            if (current is null) return new VacationResult(false, "You're not on vacation.");
            if (current.StartDate > day) db.Vacations.Remove(current);
            else if (current.ReturnDate == day.AddDays(1)) return new VacationResult(false, "Your vacation already ends today.");
            else current.EndedEarlyOn = day;
            await db.SaveChangesAsync();
        }
        return await RecalculateAsync(userId, day);
    }

    /// <summary>True once, on the first normal day after a vacation (its return date, or the day after "I'm back"):
    /// Home shows "Welcome back!" then. The vacation is marked so the note doesn't come back.</summary>
    public async Task<bool> TakeWelcomeBackAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Vacations.Where(v => v.UserId == userId && !v.WelcomeShown && v.StartDate < day).ToListAsync();
        var back = rows.FirstOrDefault(v => VacationRules.BackOn(new VacationPeriod(v.StartDate, v.ReturnDate, v.EndedEarlyOn)) == day);
        if (back is null) return false;
        back.WelcomeShown = true;
        await db.SaveChangesAsync();
        return true;
    }

    // ===== Gyms where you are (19d) =====

    /// <summary>The city the map shows while on vacation, or null (none picked, or no vacation today).</summary>
    public async Task<City?> GetVisitingCityAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Vacations.AsNoTracking().Include(v => v.VisitingCity)
            .Where(v => v.UserId == userId && v.VisitingCityId != null && v.StartDate <= day && v.ReturnDate > day).ToListAsync();
        return rows.FirstOrDefault(v => v.EndedEarlyOn is null || v.EndedEarlyOn >= day)?.VisitingCity;
    }

    /// <summary>Keeps <paramref name="cityId"/> as the city being visited, on the vacation of today. False with no
    /// vacation today (the home city stays the map's city either way).</summary>
    public async Task<bool> SetVisitingCityAsync(int userId, int cityId, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Vacations.Where(v => v.UserId == userId && v.StartDate <= day && v.ReturnDate > day).ToListAsync();
        var current = rows.FirstOrDefault(v => v.EndedEarlyOn is null || v.EndedEarlyOn >= day);
        if (current is null) return false;
        current.VisitingCityId = cityId;
        await db.SaveChangesAsync();
        return true;
    }

    // ===== Noticing a trip (19c) =====

    /// <summary>The home city to compare a fix with, or null when Home shouldn't look now: no home city, a vacation in
    /// progress or starting tomorrow, the 60-day cooldown, no streak to keep (count 0), or a look less than 6 hours ago.
    /// A missed day to settle doesn't stop it: the prompt then says what to do first.</summary>
    public async Task<AwayCheckTarget?> AwayCheckDueAsync(int userId, DateTime? now = null)
    {
        var at = now ?? AppClock.Now;
        var day = DateOnly.FromDateTime(at);
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.UserProfiles.AsNoTracking().Include(p => p.HomeCity).FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile?.HomeCity is not { } city || !VacationRules.IsAwayCheckDue(at, profile.LastAwayCheckAt)) return null;

        var streak = await streaks.GetAsync(userId, day);
        if (streak.Current == 0) return null;
        var check = VacationRules.CanStart(await StreakService.LoadVacationsAsync(db, userId), streak.Status, day);
        if (check.Refusal is VacationRefusal.AlreadyOnVacation or VacationRefusal.Cooldown) return null;
        return new AwayCheckTarget(city.Name, city.South, city.West, city.North, city.East);
    }

    /// <summary>Records a look for a trip (whether or not a fix came) and says whether to ask "On vacation?": the fix is
    /// more than 40 km outside the home city and the user hasn't said "Not on vacation" on this trip. A fix back home
    /// ends the trip, so the next one asks again. The fix itself isn't stored.</summary>
    public async Task<bool> RecordAwayFixAsync(int userId, AwayCheckTarget home, LocationFix? fix, DateTime? now = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null || fix is null) return false;   // no fix (GPS didn't answer): not a look, so it's tried again soon
        profile.LastAwayCheckAt = now ?? AppClock.Now;
        var away = fix is { } f && VacationRules.IsAway(f, home.South, home.West, home.North, home.East);
        if (fix is not null && !away) profile.VacationPromptDismissedAt = null;   // back home: the next trip asks again
        await db.SaveChangesAsync();
        return away && profile.VacationPromptDismissedAt is null;
    }

    /// <summary>"Not on vacation": not asked again until a fix sees the user back home.</summary>
    public async Task DismissAwayPromptAsync(int userId, DateTime? now = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null) return;
        profile.VacationPromptDismissedAt = now ?? AppClock.Now;
        await db.SaveChangesAsync();
    }

    /// <summary>Every frozen day of the user's vacations.</summary>
    public async Task<IReadOnlySet<DateOnly>> GetFrozenDatesAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return VacationRules.FrozenDays(await StreakService.LoadVacationsAsync(db, userId));
    }

    /// <summary>The line the UI shows for a refusal.</summary>
    public static string RefusalText(VacationCheck check) => check.Refusal switch
    {
        VacationRefusal.AlreadyOnVacation => "You're already on vacation.",
        VacationRefusal.Cooldown => $"Your next vacation is available on {check.AvailableFrom:ddd d MMM}.",
        VacationRefusal.SettleMissedDay =>
            "Log today's workout or choose what to do about yesterday first, then your vacation can start.",
        _ => ""
    };

    private async Task<VacationResult> RecalculateAsync(int userId, DateOnly day)
    {
        var streak = await streaks.RecalculateAsync(userId, day);
        return new VacationResult(true, Streak: streak, Points: await points.SyncAsync(userId, day));
    }
}
