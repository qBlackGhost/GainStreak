using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

public record DayInput(DayOfWeek Weekday, string Name);
public record SplitResult(bool Success, string? Error = null, string? Field = null);

public class SplitService(IDbContextFactory<AppDbContext> dbFactory, PointsService points)
{
    public const int MaxCustomSplits = 5;

    public async Task<List<WorkoutSplit>> GetPresetsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WorkoutSplits.AsNoTracking()
            .Include(s => s.Days.OrderBy(d => d.Order))
            .Include(s => s.Variants.OrderBy(v => v.Id))
            .Where(s => s.OwnerUserId == null)
            .OrderByDescending(s => s.IsRecommended).ThenBy(s => s.Id)
            .ToListAsync();
    }

    public async Task<List<WorkoutSplit>> GetCustomAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WorkoutSplits.AsNoTracking()
            .Include(s => s.Days.OrderBy(d => d.Order))
            .Where(s => s.OwnerUserId == userId)
            .OrderBy(s => s.Id)
            .ToListAsync();
    }

    public async Task<SplitResult> CreateAsync(int userId, string name, IReadOnlyList<DayInput> days)
    {
        if (Validate(name, days) is ({ } error, var field)) return new(false, error, field);
        name = name.Trim();

        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.WorkoutSplits.CountAsync(s => s.OwnerUserId == userId) >= MaxCustomSplits)
            return new(false, $"You can have at most {MaxCustomSplits} custom splits.");
        if (await NameTakenAsync(db, userId, name, exceptId: 0))
            return new(false, "You already have a split with that name.", FormField.Name);

        var split = new WorkoutSplit { Name = name, OwnerUserId = userId };
        var order = 1;
        foreach (var d in Normalize(days))
            split.Days.Add(new SplitDay { Weekday = d.Weekday, Name = d.Name, Order = order++ });

        db.WorkoutSplits.Add(split);
        await db.SaveChangesAsync();
        return new(true);
    }

    public async Task<SplitResult> UpdateAsync(int userId, int splitId, string name, IReadOnlyList<DayInput> days)
    {
        if (Validate(name, days) is ({ } error, var field)) return new(false, error, field);
        name = name.Trim();

        await using var db = await dbFactory.CreateDbContextAsync();
        var split = await db.WorkoutSplits.Include(s => s.Days)
            .FirstOrDefaultAsync(s => s.Id == splitId && s.OwnerUserId == userId);
        if (split is null) return new(false, "Split not found.");
        if (await NameTakenAsync(db, userId, name, splitId))
            return new(false, "You already have a split with that name.", FormField.Name);

        split.Name = name;
        var wanted = Normalize(days);

        foreach (var gone in split.Days
                     .Where(d => d.Weekday is null || wanted.All(w => w.Weekday != d.Weekday)).ToList())
            db.SplitDays.Remove(gone);

        var order = 1;
        foreach (var w in wanted)
        {
            var existing = split.Days.FirstOrDefault(d => d.Weekday == w.Weekday);
            if (existing is null)
                split.Days.Add(new SplitDay { Weekday = w.Weekday, Name = w.Name, Order = order });
            else { existing.Name = w.Name; existing.Order = order; }
            order++;
        }

        await db.SaveChangesAsync();

        // Editing the active split changes the user's week from today on (past days keep their old schedule)
        if (await db.Users.AnyAsync(u => u.Id == userId && u.SelectedSplitId == splitId))
        {
            await UpsertScheduleAsync(db, userId, splitId,
                WeekPattern.From(WeekSchedule.FromWeekdays(wanted.Select(w => w.Weekday))));
            await points.SyncAsync(userId);   // the new week can complete (or no longer complete) this week's bonus
        }

        return new(true);
    }

    public async Task DeleteAsync(int userId, int splitId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.WorkoutSplits.Where(s => s.Id == splitId && s.OwnerUserId == userId).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Presets need a variant (their weekly layout) and a start day; custom splits carry their own
    /// weekdays, so both are stored as null for them. Also records the new week in the schedule history.
    /// </summary>
    public async Task<bool> SelectAsync(int userId, int splitId, int? variantId, DayOfWeek? startDay)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var split = await db.WorkoutSplits.AsNoTracking().Include(s => s.Days).FirstOrDefaultAsync(s =>
            s.Id == splitId && (s.OwnerUserId == null || s.OwnerUserId == userId));
        if (split is null) return false;

        string weekPattern;
        if (split.OwnerUserId is null)
        {
            var variant = variantId is null ? null
                : await db.SplitVariants.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == variantId && v.SplitId == splitId);
            if (variant is null) return false;

            startDay ??= DayOfWeek.Monday;
            weekPattern = WeekPattern.From(WeekSchedule.Build(variant.Pattern, startDay.Value, 1));
        }
        else
        {
            var weekdays = split.Days.Where(d => d.Weekday is not null).Select(d => d.Weekday!.Value).ToList();
            if (weekdays.Count == 0) return false;

            variantId = null;
            startDay = null;
            weekPattern = WeekPattern.From(WeekSchedule.FromWeekdays(weekdays));
        }

        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.SelectedSplitId, (int?)splitId)
                .SetProperty(x => x.SelectedVariantId, variantId)
                .SetProperty(x => x.SplitStartDay, startDay));

        await UpsertScheduleAsync(db, userId, splitId, weekPattern);
        await points.SyncAsync(userId);
        return true;
    }

    /// <summary>One history row per user per day: changing the schedule twice in a day updates the same row.</summary>
    private static async Task UpsertScheduleAsync(AppDbContext db, int userId, int splitId, string weekPattern)
    {
        var today = AppClock.Today;
        var row = await db.UserSchedules.FirstOrDefaultAsync(s => s.UserId == userId && s.EffectiveFrom == today);
        if (row is null)
            db.UserSchedules.Add(new UserSchedule
                { UserId = userId, EffectiveFrom = today, WeekPattern = weekPattern, SplitId = splitId });
        else
        {
            row.WeekPattern = weekPattern;
            row.SplitId = splitId;
        }
        await db.SaveChangesAsync();
    }

    // Monday-first, trimmed
    private static List<DayInput> Normalize(IEnumerable<DayInput> days) =>
        days.Select(d => new DayInput(d.Weekday, d.Name.Trim()))
            .OrderBy(d => ((int)d.Weekday + 6) % 7)
            .ToList();

    private static Task<bool> NameTakenAsync(AppDbContext db, int userId, string name, int exceptId)
    {
        var lowered = name.ToLower();
        return db.WorkoutSplits.AnyAsync(s =>
            s.OwnerUserId == userId && s.Id != exceptId && s.Name.ToLower() == lowered);
    }

    private static (string? Error, string? Field) Validate(string name, IReadOnlyList<DayInput> days)
    {
        if (string.IsNullOrWhiteSpace(name)) return ("Give your split a name.", FormField.Name);
        if (name.Trim().Length > 40) return ("Split name must be at most 40 characters.", FormField.Name);
        if (days.Count == 0) return ("Pick at least one training day.", FormField.Days);
        if (days.Any(d => string.IsNullOrWhiteSpace(d.Name))) return ("Every selected day needs a name.", FormField.Days);
        if (days.Any(d => d.Name.Trim().Length > 20)) return ("Day names must be at most 20 characters.", FormField.Days);
        if (days.Select(d => d.Weekday).Distinct().Count() != days.Count) return ("A weekday was selected twice.", FormField.Days);
        return (null, null);
    }
}
