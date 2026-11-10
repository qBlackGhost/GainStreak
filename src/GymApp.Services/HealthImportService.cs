using System;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>
/// Phase 20c: what's read from Apple Health goes into the app here, by the rules in <see cref="HealthImport"/>.
/// Weigh-ins fill days without a typed one (<see cref="WeightLog.FromHealth"/>), profile values fill empty fields and
/// the rest is offered, steps suggest an activity level. Nothing read here ever leaves the phone (decision 2).
/// </summary>
public class HealthImportService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>Where the weigh-in import starts: <see cref="HealthImport.WeighInDaysBack"/> days before Health was
    /// turned on, and the anchor of the last import. Null when Health is off.</summary>
    public async Task<(DateTime From, byte[]? Anchor)?> WeightImportStartAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var p = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.HealthEnabled);
        if (p is null) return null;
        var on = DateOnly.FromDateTime(p.HealthEnabledAt ?? AppClock.Now);
        return (on.AddDays(-HealthImport.WeighInDaysBack).ToDateTime(TimeOnly.MinValue), p.HealthWeightAnchor);
    }

    /// <summary>Imports <paramref name="weights"/> and keeps their anchor. A day's earlier import is replaced by a later
    /// one; a typed weigh-in is never touched. Returns how many days were added or changed.</summary>
    public async Task<int> ImportWeighInsAsync(int userId, HealthWeights weights, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId && p.HealthEnabled);
        if (profile is null) return 0;
        var earliest = DateOnly.FromDateTime(profile.HealthEnabledAt ?? AppClock.Now).AddDays(-HealthImport.WeighInDaysBack);

        var logs = await db.WeightLogs.Where(w => w.UserId == userId && w.Date >= earliest).ToListAsync();
        var typed = logs.Where(w => !w.FromHealth).Select(w => w.Date).ToHashSet();
        var changed = 0;
        foreach (var (date, kg) in HealthImport.WeighInsToImport(weights.Samples, typed, earliest, day))
        {
            var entry = logs.FirstOrDefault(w => w.Date == date);
            if (entry is null)
            {
                db.WeightLogs.Add(new WeightLog { UserId = userId, Date = date, WeightKg = kg, FromHealth = true });
                changed++;
            }
            else if (entry.WeightKg != kg)
            {
                entry.WeightKg = kg;
                changed++;
            }
        }
        profile.HealthWeightAnchor = weights.Anchor;
        await db.SaveChangesAsync();
        return changed;
    }

    /// <summary>Fills empty profile fields from Health and returns what differs from typed values, unless the user
    /// already kept theirs over exactly these values. Null when Health is off.</summary>
    public async Task<(bool Filled, HealthProfileOffer? Offer)?> ApplyProfileAsync(int userId, HealthProfile health, DateOnly? today = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var p = await db.UserProfiles.FirstOrDefaultAsync(x => x.UserId == userId && x.HealthEnabled);
        if (p is null) return null;
        var (fill, offer) = HealthImport.Compare(health, p.HeightCm, p.BirthDate, p.Sex, today ?? AppClock.Today);
        var filled = false;
        if (fill.HeightCm is { } h) { p.HeightCm = h; filled = true; }
        if (fill.BirthDate is { } b) { p.BirthDate = b; filled = true; }
        if (fill.Sex is { } s) { p.Sex = s; filled = true; }
        if (filled)
        {
            p.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return (filled, offer.IsEmpty || HealthImport.OfferKey(offer) == p.HealthProfileDismissed ? null : offer);
    }

    /// <summary>"Use Health's" takes the offered values; "Keep mine" remembers them so they aren't offered again.</summary>
    public async Task AnswerProfileOfferAsync(int userId, HealthProfileOffer offer, bool useHealth)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await ProfileService.ProfileForWriteAsync(db, userId) is not { } p) return;
        if (useHealth)
        {
            if (offer.HeightCm is { } h) p.HeightCm = h;
            if (offer.BirthDate is { } b) p.BirthDate = b;
            if (offer.Sex is { } s) p.Sex = s;
        }
        else p.HealthProfileDismissed = HealthImport.OfferKey(offer);
        p.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>The activity level the steps suggest, when it's new to the user (<see cref="HealthImport.StepsSuggestion"/>).</summary>
    public async Task<ActivityLevel?> StepsSuggestionAsync(int userId, double? averageSteps)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var p = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.HealthEnabled);
        return p is null ? null : HealthImport.StepsSuggestion(averageSteps, p.ActivityLevel, p.HealthStepsSuggestionSeen);
    }

    /// <summary>"Use it" sets the level; either answer means this level isn't suggested again.</summary>
    public async Task AnswerStepsSuggestionAsync(int userId, ActivityLevel level, bool use)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await ProfileService.ProfileForWriteAsync(db, userId) is not { } p) return;
        if (use) p.ActivityLevel = level;
        p.HealthStepsSuggestionSeen = level;
        p.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
