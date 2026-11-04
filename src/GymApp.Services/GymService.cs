using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>The picker's list for one city, read from the database only (never the network).</summary>
public record GymListResult(City City, IReadOnlyList<GymRow> Rows);

public record GymChoice(int Id, string Name);

/// <summary>Everything Home needs about gyms for today. Gym is today's gym (null if none, or nothing logged);
/// LastUsed is offered as the one-tap button in the check-in prompt.</summary>
public record TodayGymInfo(bool LoggedToday, GymChoice? Gym, GymChoice? LastUsed, bool AskAfterCheckIn,
    GymChoice? HomeGym = null, bool? ShareGymCheckIns = null)
{
    /// <summary>The gym the check-in prompt offers first: your gym, else the last one you used.</summary>
    public GymChoice? Suggested => HomeGym ?? LastUsed;
}

public record GymResult(bool Success, string? Error = null);

/// <summary>Reactivated = the name matched one of the user's deleted-but-kept gyms in that city, so that gym came back
/// (with its history) instead of a new one being made.</summary>
public record CustomGymResult(bool Success, string? Error = null, int? GymId = null, bool Reactivated = false, string? Field = null);

/// <summary>An address the user picked from an address search inside the city, with its position.</summary>
public record PickedAddress(string Text, double Latitude, double Longitude);

public enum CustomGymDeleteOutcome
{
    Deleted,       // no workout used it, so it is gone
    Deactivated    // workouts used it: hidden from the picker, kept in history and summaries
}

public record DeleteGymResult(bool Success, CustomGymDeleteOutcome? Outcome = null, string? Error = null);

/// <summary>Which gyms a user can see and pick, and the gym on today's workout log. Gyms never earn points
/// and never change the streak: the workout is already logged before a gym is picked.</summary>
public class GymService(IDbContextFactory<AppDbContext> dbFactory, ISocialSyncHooks? sync = null)
{
    private static DateOnly Today => AppClock.Today;

    /// <summary>Active OSM gyms in the city plus this user's own active custom gyms there. Null if the city doesn't exist.</summary>
    public async Task<GymListResult?> GetGymsForCityAsync(int userId, int cityId, string? search = null, GymSort sort = GymSort.Name)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var city = await db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cityId);
        if (city is null) return null;

        var gyms = await Pickable(db, userId)
            .Where(g => g.CityId == cityId)
            .Select(g => new GymListItem(g.Id, g.Name, g.Address ?? g.LookedUpAddress, g.Latitude, g.Longitude,
                g.OwnerUserId != null, g.Address == null && g.LookedUpAddress != null))
            .ToListAsync();

        return new GymListResult(city, GymList.Arrange(gyms, search, sort, city.CenterLatitude, city.CenterLongitude));
    }

    /// <summary>Sets (or with null, clears) the gym on today's workout log. Only today's log can change, so a
    /// past day can never get a gym or lose one. The gym must be active and be an OSM gym or the user's own.</summary>
    public async Task<GymResult> SetGymForTodayAsync(int userId, int? gymId, DateOnly? today = null)
    {
        var day = today ?? Today;
        await using var db = await dbFactory.CreateDbContextAsync();

        var log = await db.WorkoutLogs.FirstOrDefaultAsync(w => w.UserId == userId && w.Date == day);
        if (log is null) return new(false, "Log today's workout first, then add the gym.");

        if (gymId is { } id && !await Pickable(db, userId).AnyAsync(g => g.Id == id))
            return new(false, "That gym isn't available any more. Pick another one.");

        log.GymId = gymId;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "Could not save the gym."); }
        (sync ?? NoSocialSync.Instance).MarkCheckInsDirty(userId);
        return new(true);
    }

    /// <summary>The gym on the user's most recent log that has one, derived and never stored. Null when that
    /// gym has since been deactivated, so a closed gym is never offered.</summary>
    public async Task<GymChoice?> GetLastUsedGymAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await LastUsedAsync(db, userId, before: null);
    }

    /// <summary>One gym the user may pick, as the list shows it; null if it's closed, gone or someone else's.</summary>
    public async Task<GymListItem?> GetPickableGymAsync(int userId, int gymId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await Pickable(db, userId).Where(g => g.Id == gymId)
            .Select(g => new GymListItem(g.Id, g.Name, g.Address ?? g.LookedUpAddress, g.Latitude, g.Longitude,
                g.OwnerUserId != null, g.Address == null && g.LookedUpAddress != null))
            .FirstOrDefaultAsync();
    }

    /// <summary>The user's visits per gym in a calendar month (gym id → workouts logged there), for the map's gym card.</summary>
    public async Task<IReadOnlyDictionary<int, int>> GetMonthVisitsAsync(int userId, int year, int month)
    {
        var (first, last) = GymSummaries.MonthRange(year, month);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WorkoutLogs.AsNoTracking()
            .Where(w => w.UserId == userId && w.GymId != null && w.Date >= first && w.Date <= last)
            .GroupBy(w => w.GymId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
    }

    /// <summary>One call for Home: today's log and gym, the last-used gym before today, and the prompt setting.</summary>
    public async Task<TodayGymInfo> GetTodayAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? Today;
        await using var db = await dbFactory.CreateDbContextAsync();

        var log = await db.WorkoutLogs.AsNoTracking()
            .Where(w => w.UserId == userId && w.Date == day)
            .Select(w => new { Gym = w.Gym == null ? null : new GymChoice(w.Gym.Id, w.Gym.Name) })
            .FirstOrDefaultAsync();
        var profile = await db.UserProfiles.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new { p.AskForGymAfterCheckIn, p.ShareGymCheckIns, p.HomeGymId }).FirstOrDefaultAsync();
        var home = profile?.HomeGymId is { } homeId
            ? await Pickable(db, userId).Where(g => g.Id == homeId).Select(g => new GymChoice(g.Id, g.Name)).FirstOrDefaultAsync()
            : null;

        return new TodayGymInfo(log is not null, log?.Gym, await LastUsedAsync(db, userId, before: day),
            profile?.AskForGymAfterCheckIn ?? true, home, profile?.ShareGymCheckIns);
    }

    private static async Task<GymChoice?> LastUsedAsync(AppDbContext db, int userId, DateOnly? before)
    {
        var last = await db.WorkoutLogs.AsNoTracking()
            .Where(w => w.UserId == userId && w.GymId != null && (before == null || w.Date < before))
            .OrderByDescending(w => w.Date)
            .Select(w => new { w.Gym!.Id, w.Gym.Name, w.Gym.IsActive, w.Gym.OwnerUserId })
            .FirstOrDefaultAsync();
        return last is { IsActive: true } && (last.OwnerUserId is null || last.OwnerUserId == userId)
            ? new GymChoice(last.Id, last.Name)
            : null;
    }

    // ===== Custom gyms =====

    /// <summary>Adds a gym OpenStreetMap doesn't have, in the picker's current city, at a picked address and its position
    /// (required since 18c, so workouts there can be verified; gyms added before keep working without one). At most 10
    /// active per user; names are unique per user and city, ignoring case (two users may use the same name).</summary>
    public async Task<CustomGymResult> AddCustomGymAsync(int userId, int cityId, string name, PickedAddress? address)
    {
        var cleanName = CustomGyms.Clean(name);
        if (CustomGyms.CheckName(cleanName) is { } nameError) return new(false, nameError, Field: FormField.Name);
        if (address is null) return new(false, CustomGyms.AddressRequired, Field: FormField.Address);
        if (CheckAddress(address) is { } addressError) return new(false, addressError, Field: FormField.Address);

        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Cities.AnyAsync(c => c.Id == cityId)) return new(false, "That city is no longer available.");

        var activeCount = await db.Gyms.CountAsync(g => g.OwnerUserId == userId && g.IsActive);
        var mineHere = await db.Gyms.Where(g => g.OwnerUserId == userId && g.CityId == cityId).ToListAsync();
        var same = mineHere.FirstOrDefault(g => CustomGyms.SameName(g.Name, cleanName));

        if (same is { IsActive: true }) return new(false, $"You already have a gym called \"{same.Name}\" here.", Field: FormField.Name);
        if (activeCount >= CustomGyms.MaxPerUser)
            return new(false, $"You can have up to {CustomGyms.MaxPerUser} custom gyms. Delete one to add another.");

        // A gym deleted earlier but kept for history comes back under the same row, so its visits stay together
        var gym = same;
        if (gym is null)
        {
            gym = new Gym { OwnerUserId = userId, CityId = cityId };
            db.Gyms.Add(gym);
        }
        gym.Name = cleanName;
        SetAddress(gym, address);
        gym.IsActive = true;

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "Could not save the gym."); }
        return new(true, GymId: gym.Id, Reactivated: same is not null);
    }

    /// <summary>Renames a custom gym or changes its address. A new address is required (it can't be cleared, 18c); with
    /// keepAddress the current address and position stay as they are, whatever was passed, so a gym from before
    /// can still be renamed without one. Owner only; the city stays the same.</summary>
    public async Task<CustomGymResult> UpdateCustomGymAsync(int userId, int gymId, string name, PickedAddress? address,
        bool keepAddress = false)
    {
        var cleanName = CustomGyms.Clean(name);
        if (CustomGyms.CheckName(cleanName) is { } nameError) return new(false, nameError, Field: FormField.Name);
        if (!keepAddress && address is null) return new(false, CustomGyms.AddressRequired, Field: FormField.Address);
        if (!keepAddress && CheckAddress(address) is { } addressError) return new(false, addressError, Field: FormField.Address);

        await using var db = await dbFactory.CreateDbContextAsync();
        var gym = await db.Gyms.FirstOrDefaultAsync(g => g.Id == gymId && g.OwnerUserId == userId && g.IsActive);
        if (gym is null) return new(false, "That gym can't be edited.");   // not yours, an OSM gym, or deleted

        var others = await db.Gyms.Where(g => g.OwnerUserId == userId && g.CityId == gym.CityId && g.Id != gymId)
            .Select(g => g.Name).ToListAsync();
        if (others.FirstOrDefault(n => CustomGyms.SameName(n, cleanName)) is { } taken)
            return new(false, $"You already have a gym called \"{taken}\" here.", Field: FormField.Name);

        var renamed = gym.Name != cleanName;
        gym.Name = cleanName;
        if (!keepAddress) SetAddress(gym, address);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "Could not save the gym."); }

        // Shared check-ins carry the gym's name (never a custom gym's address), so only a rename needs a resync
        var since = Today.AddDays(-(SocialSync.CheckInDays - 1));
        if (renamed && await db.WorkoutLogs.AnyAsync(w => w.UserId == userId && w.GymId == gymId && w.Date >= since))
            (sync ?? NoSocialSync.Instance).MarkCheckInsDirty(userId);
        return new(true, GymId: gym.Id);
    }

    private static string? CheckAddress(PickedAddress? a)
    {
        if (a is null) return null;
        if (CustomGyms.CheckAddress(CustomGyms.CleanAddress(a.Text)) is { } error) return error;
        if (CustomGyms.CleanAddress(a.Text) is null) return "Pick an address from the search results.";
        if (!double.IsFinite(a.Latitude) || !double.IsFinite(a.Longitude) || Math.Abs(a.Latitude) > 90 || Math.Abs(a.Longitude) > 180)
            return "That address has no valid position.";
        return null;
    }

    private static void SetAddress(Gym gym, PickedAddress? a)
    {
        gym.Address = a is null ? null : CustomGyms.CleanAddress(a.Text);
        gym.Latitude = a?.Latitude;
        gym.Longitude = a?.Longitude;
    }

    /// <summary>Deletes a custom gym if no workout used it; otherwise deactivates it, so it leaves the picker but
    /// stays in history and summaries. Says which one happened. Owner only.</summary>
    public async Task<DeleteGymResult> DeleteCustomGymAsync(int userId, int gymId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var gym = await db.Gyms.FirstOrDefaultAsync(g => g.Id == gymId && g.OwnerUserId == userId);
        if (gym is null) return new(false, Error: "That gym can't be deleted.");

        CustomGymDeleteOutcome outcome;
        if (await db.WorkoutLogs.AnyAsync(w => w.GymId == gymId))
        {
            gym.IsActive = false;
            outcome = CustomGymDeleteOutcome.Deactivated;
        }
        else
        {
            db.Gyms.Remove(gym);
            outcome = CustomGymDeleteOutcome.Deleted;
        }

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, Error: "Could not delete the gym."); }
        return new(true, outcome);
    }

    /// <summary>Gyms this user may pick: active, and from OSM or their own.</summary>
    /// <summary>Gyms a user may pick (for today or as their gym): active, and from OSM or their own.</summary>
    internal static IQueryable<Gym> Pickable(AppDbContext db, int userId) =>
        db.Gyms.Where(g => g.IsActive && (g.OwnerUserId == null || g.OwnerUserId == userId));
}
