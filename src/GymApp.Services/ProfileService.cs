using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>What the profile form saves. Everything is metric; empty fields are allowed.</summary>
public record ProfileInput(Sex? Sex, DateOnly? BirthDate, double? HeightCm, ActivityLevel? ActivityLevel, UnitSystem Units);

public record ProfileResult(bool Success, string? Error = null);

public record WeightResult(bool Success, string? Error = null, WeightLog? Entry = null);

/// <summary>Maintenance calories, or the reason there is no estimate yet (Kcal is null then).</summary>
public record MaintenanceEstimate(int? Kcal, string? Reason = null, int? Bmr = null, double? ActivityFactor = null);

public record ProfileSummary(
    string Username,
    string? SplitName,
    int CurrentStreak,
    int LongestStreak,
    UserProfile Profile,
    WeightLog? CurrentWeight,
    IReadOnlyList<WeightLog> RecentWeights,
    MaintenanceEstimate Maintenance);

public class ProfileService(IDbContextFactory<AppDbContext> dbFactory)
{
    public const int RecentWeightCount = 10;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Everything the profile screen shows. A user without a profile row gets an empty one
    /// (not saved until they press Save).</summary>
    public async Task<ProfileSummary?> GetAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? Today;
        await using var db = await dbFactory.CreateDbContextAsync();

        var user = await db.Users.AsNoTracking()
            .Include(u => u.SelectedSplit)
            .Include(u => u.SelectedVariant)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;

        var profile = await db.UserProfiles.AsNoTracking().Include(p => p.HomeCity)
                          .FirstOrDefaultAsync(p => p.UserId == userId)
                      ?? new UserProfile { UserId = userId };

        var recent = await db.WeightLogs.AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.Date)
            .Take(RecentWeightCount)
            .ToListAsync();
        var current = recent.FirstOrDefault();

        string? splitName = user.SelectedSplit is null ? null
            : user.SelectedVariant is null ? user.SelectedSplit.Name
            : $"{user.SelectedSplit.Name} ({user.SelectedVariant.Name})";

        return new ProfileSummary(user.Username, splitName, user.CurrentStreak, user.LongestStreak,
            profile, current, recent, Estimate(profile, current?.WeightKg, day));
    }

    /// <summary>Creates or updates the user's profile. Filled-in fields must be in range.</summary>
    public async Task<ProfileResult> SaveAsync(int userId, ProfileInput input, DateOnly? today = null)
    {
        var day = today ?? Today;
        if (Validate(input, day) is { } error) return new(false, error);

        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Users.AnyAsync(u => u.Id == userId)) return new(false, "User not found.");

        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null)
        {
            profile = new UserProfile { UserId = userId };
            db.UserProfiles.Add(profile);
        }

        profile.Sex = input.Sex;
        profile.BirthDate = input.BirthDate;
        profile.HeightCm = input.HeightCm is { } h ? Math.Round(h, 1) : null;
        profile.ActivityLevel = input.ActivityLevel;
        profile.Units = input.Units;
        profile.UpdatedAt = DateTime.UtcNow;

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "Could not save your profile."); }
        return new(true);
    }

    /// <summary>Sets the home city the gym picker opens with, or clears it (null). Saved straight away,
    /// separately from the body data form. Creates the profile row if there is none yet.</summary>
    public async Task<ProfileResult> SetHomeCityAsync(int userId, int? cityId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Users.AnyAsync(u => u.Id == userId)) return new(false, "User not found.");
        if (cityId is { } id && !await db.Cities.AnyAsync(c => c.Id == id)) return new(false, "That city is no longer available.");

        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null)
        {
            profile = new UserProfile { UserId = userId };
            db.UserProfiles.Add(profile);
        }
        profile.HomeCityId = cityId;
        profile.UpdatedAt = DateTime.UtcNow;

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "Could not save your home city."); }
        return new(true);
    }

    /// <summary>Logs today's weight. Today only, like workouts and sets; a second weigh-in today
    /// replaces the first one.</summary>
    public async Task<WeightResult> LogWeightAsync(int userId, double weightKg, DateOnly? today = null)
    {
        var day = today ?? Today;
        if (!double.IsFinite(weightKg)) return new(false, "Enter a valid weight.");
        if (BodyMetrics.CheckWeight(weightKg) is { } error) return new(false, error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.WeightLogs.FirstOrDefaultAsync(w => w.UserId == userId && w.Date == day);
        if (entry is null)
        {
            entry = new WeightLog { UserId = userId, Date = day };
            db.WeightLogs.Add(entry);
        }
        entry.WeightKg = Math.Round(weightKg, 2);

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "Could not save your weight."); }
        return new(true, Entry: entry);
    }

    /// <summary>Removes one of the user's own weigh-ins (to fix a typo).</summary>
    public async Task<bool> DeleteWeightAsync(int userId, int weightLogId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WeightLogs
            .Where(w => w.Id == weightLogId && w.UserId == userId)
            .ExecuteDeleteAsync() > 0;
    }

    /// <summary>Maintenance calories from the profile and the current weight. Pure, so it is easy to test.</summary>
    public static MaintenanceEstimate Estimate(UserProfile profile, double? weightKg, DateOnly today)
    {
        var missing = new List<string>();
        if (profile.Sex is null) missing.Add("sex");
        if (profile.BirthDate is null) missing.Add("birth date");
        if (profile.HeightCm is null) missing.Add("height");
        if (weightKg is null) missing.Add("weight");
        if (profile.ActivityLevel is null) missing.Add("activity level");
        if (missing.Count > 0)
            return new(null, $"Add your {JoinList(missing)} to see your maintenance calories.");

        var age = BodyMetrics.AgeOn(profile.BirthDate!.Value, today);
        if (age < BodyLimits.AdultAge)
            return new(null, $"Automatic calorie estimates are only shown from age {BodyLimits.AdultAge}. " +
                             "You can still set your own calorie goal.");

        var bmr = BodyMetrics.Bmr(profile.Sex!.Value, weightKg!.Value, profile.HeightCm!.Value, age);
        var factor = BodyMetrics.ActivityFactor(profile.ActivityLevel!.Value);
        return new(BodyMetrics.RoundTo10(bmr * factor), Bmr: BodyMetrics.RoundTo10(bmr), ActivityFactor: factor);
    }

    private static string? Validate(ProfileInput input, DateOnly today)
    {
        if (input.Sex is { } sex && !Enum.IsDefined(sex)) return "Pick male or female.";
        if (!Enum.IsDefined(input.Units)) return "Pick a unit system.";
        if (input.BirthDate is { } b && BodyMetrics.CheckBirthDate(b, today) is { } e1) return e1;
        if (input.HeightCm is { } h && !double.IsFinite(h)) return "Enter a valid height.";
        if (input.HeightCm is { } h2 && BodyMetrics.CheckHeight(h2) is { } e2) return e2;
        if (input.ActivityLevel is { } a && !Enum.IsDefined(a)) return "Pick an activity level.";
        return null;
    }

    private static string JoinList(List<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}"
    };
}
