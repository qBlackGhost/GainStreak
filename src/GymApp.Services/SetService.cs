using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;
using CorePrAdjustment = GymApp.Core.PrAdjustment;
using DbPrAdjustment = GymApp.Data.Models.PrAdjustment;

namespace GymApp.Services;

/// <summary>One row on the exercise screen.</summary>
public record SetInput(SetKind Kind, int Reps, double WeightKg);

/// <summary>How the weight is typed for a bar exercise (see BarWeight). Default: the total, no bar.</summary>
public record BarSetting(bool PerSide, double? BarKg)
{
    public static readonly BarSetting Total = new(false, null);
}

public record SetResult(bool Success, string? Error = null, string? Field = null);

/// <summary>Everything the PR logic needs for one exercise. Sets and Adjustments are the raw data, so the
/// view model can also recompute a PR from a subset (for the prompt while typing).</summary>
public record ExerciseHistory(IReadOnlyList<SetEntry> Sets, IReadOnlyList<CorePrAdjustment> Adjustments)
{
    /// <summary>The PR right now, with the full history of entries.</summary>
    public PrHistory Pr => PrCalculator.Compute(Sets, Adjustments);

    /// <summary>The PR as it stood before <paramref name="day"/> started (that day's sets and adjustments ignored).</summary>
    public PrHistory PrBefore(DateOnly day) =>
        PrCalculator.Compute(Sets.Where(s => s.Date < day), Adjustments.Where(a => a.Date < day));
}

/// <summary>A PR for the dashboard: the set that beat the previous best.</summary>
public record RecentPr(int ExerciseId, string ExerciseName, DateOnly Date, double WeightKg, int Reps);

/// <param name="workouts">Keeps today's workout log's completion in line with the saved sets (Phase 18). Absent in
/// tests that only check sets.</param>
public class SetService(IDbContextFactory<AppDbContext> dbFactory, WorkoutService? workouts = null)
{
    public const int MaxSetsPerExercisePerDay = 30;
    public const int MaxReps = 100;
    public const double MaxWeightKg = 1000;
    public const double MaxOneRepMaxKg = 2000;
    public const double MaxBarKg = 100;

    private static DateOnly Today() => AppClock.Today;

    /// <summary>The sets logged for this exercise on <paramref name="today"/> (default: the real today).</summary>
    public async Task<List<SetEntry>> GetDayAsync(int userId, int exerciseId, DateOnly? today = null)
    {
        var date = today ?? Today();
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.SetLogs.AsNoTracking()
            .Where(s => s.UserId == userId && s.ExerciseId == exerciseId && s.Date == date)
            .OrderBy(s => s.SetNumber)
            .ToListAsync();
        return rows.Select(ToEntry).ToList();
    }

    /// <summary>
    /// Replaces today's sets for this exercise with the given rows (numbered 1..n in order). One call covers
    /// add, edit and remove; an empty list clears the day. All or nothing, and a rejected save keeps the old
    /// sets. Today only (no backdating); <paramref name="today"/> is for tests. Never touches the streak.
    /// </summary>
    public async Task<SetResult> SaveDayAsync(
        int userId, int exerciseId, IReadOnlyList<SetInput> sets, DateOnly? today = null)
    {
        if (sets.Count > MaxSetsPerExercisePerDay)
            return new(false, $"At most {MaxSetsPerExercisePerDay} sets per exercise per day.");
        foreach (var s in sets)
            if (ValidateSet(s.Reps, s.WeightKg) is ({ } error, _)) return new(false, error);

        var date = today ?? Today();
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await ExerciseAvailableAsync(db, userId, exerciseId)) return new(false, "Exercise not found.");

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.SetLogs
                .Where(s => s.UserId == userId && s.ExerciseId == exerciseId && s.Date == date)
                .ExecuteDeleteAsync();

            var number = 0;
            foreach (var s in sets)
                db.SetLogs.Add(new SetLog
                {
                    UserId = userId, ExerciseId = exerciseId, Date = date, SetNumber = ++number,
                    Kind = s.Kind, Reps = s.Reps, WeightKg = Math.Round(s.WeightKg, 2)
                });

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            return new(false, "Could not save your sets.");
        }

        // The sets are saved; a failed refresh only leaves the log's count for the next save
        if (workouts is not null)
            try { await workouts.RefreshCompletionAsync(userId, date); }
            catch (Exception) { }
        return new(true);
    }

    /// <summary>All sets and PR adjustments for one exercise, for the PR display, the history list and the prompt.</summary>
    public async Task<ExerciseHistory> GetHistoryAsync(int userId, int exerciseId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var sets = await db.SetLogs.AsNoTracking()
            .Where(s => s.UserId == userId && s.ExerciseId == exerciseId)
            .ToListAsync();
        var adjustments = await db.PrAdjustments.AsNoTracking()
            .Where(a => a.UserId == userId && a.ExerciseId == exerciseId)
            .ToListAsync();

        return new ExerciseHistory(
            sets.Select(ToEntry).ToList(),
            adjustments.Select(a => new CorePrAdjustment(a.Date, a.WeightKg, a.Reps, a.OneRepMaxKg)).ToList());
    }

    /// <summary>Sets from the most recent earlier session, for the "pre-fill from last time" button.</summary>
    public async Task<IReadOnlyList<SetEntry>> GetLastSessionAsync(int userId, int exerciseId, DateOnly? today = null)
    {
        var history = await GetHistoryAsync(userId, exerciseId);
        return PrCalculator.LastSession(history.Sets, today ?? Today());
    }

    /// <summary>
    /// The user's most recent PRs across all exercises, newest first, for the dashboard. Only real PRs count (a set
    /// that beat the best), not the first set of an exercise or a manual correction. Several PRs for one exercise on
    /// one day show once, with the best of them. Ties on a date are ordered by exercise name.
    /// </summary>
    public async Task<IReadOnlyList<RecentPr>> GetRecentPrsAsync(int userId, int count = 3)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        // Just the columns the PR rule reads: this is every working set the user ever logged
        var sets = await db.SetLogs.Where(s => s.UserId == userId && s.Kind == SetKind.Working)
            .Select(s => new { s.ExerciseId, Entry = new SetEntry(s.Date, s.SetNumber, s.Kind, s.Reps, s.WeightKg) })
            .ToListAsync();
        var adjustments = await db.PrAdjustments.AsNoTracking().Where(a => a.UserId == userId).ToListAsync();
        var ids = sets.Select(s => s.ExerciseId).Distinct().ToList();
        var names = await db.Exercises.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name);
        var adjustmentsBy = adjustments.ToLookup(a => a.ExerciseId);

        return sets.GroupBy(s => s.ExerciseId)
            .SelectMany(g => PrCalculator.Compute(g.Select(s => s.Entry),
                    adjustmentsBy[g.Key].Select(a => new CorePrAdjustment(a.Date, a.WeightKg, a.Reps, a.OneRepMaxKg)))
                .Entries.Where(e => e.Kind == PrEntryKind.Pr)
                .GroupBy(e => e.Date)
                .Select(day => day.Last())
                .Select(e => new RecentPr(g.Key, names.GetValueOrDefault(g.Key, "Exercise"), e.Date, e.WeightKg, e.Reps)))
            .OrderByDescending(p => p.Date).ThenBy(p => p.ExerciseName)
            .Take(count)
            .ToList();
    }

    /// <summary>For the Home hint: sets were logged today, whatever the exercise.</summary>
    public async Task<bool> HasSetsTodayAsync(int userId, DateOnly? today = null)
    {
        var date = today ?? Today();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SetLogs.AnyAsync(s => s.UserId == userId && s.Date == date);
    }

    /// <summary>
    /// Manually sets the current best ("modify the PR"). From today on it replaces the derived PR and later
    /// sets are compared against it. oneRepMaxKg null = estimate with Epley. One adjustment per exercise per
    /// day: setting it twice in a day updates the same row.
    /// </summary>
    public async Task<SetResult> SetPrAsync(
        int userId, int exerciseId, double weightKg, int reps, double? oneRepMaxKg = null, DateOnly? today = null)
    {
        if (ValidateSet(reps, weightKg) is ({ } error, var field)) return new(false, error, field);
        if (oneRepMaxKg is { } rm && (!double.IsFinite(rm) || rm < weightKg || rm > MaxOneRepMaxKg))
            return new(false, $"The 1RM must be at least the weight and at most {MaxOneRepMaxKg:0} kg.", FormField.OneRepMax);

        var date = today ?? Today();
        weightKg = Math.Round(weightKg, 2);
        oneRepMaxKg = oneRepMaxKg is { } v ? Math.Round(v, 2) : null;

        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await ExerciseAvailableAsync(db, userId, exerciseId)) return new(false, "Exercise not found.");

        var row = await db.PrAdjustments.FirstOrDefaultAsync(a =>
            a.UserId == userId && a.ExerciseId == exerciseId && a.Date == date);
        if (row is null)
            db.PrAdjustments.Add(new DbPrAdjustment
            {
                UserId = userId, ExerciseId = exerciseId, Date = date,
                WeightKg = weightKg, Reps = reps, OneRepMaxKg = oneRepMaxKg
            });
        else
        {
            row.WeightKg = weightKg;
            row.Reps = reps;
            row.OneRepMaxKg = oneRepMaxKg;
        }

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "Could not save the PR."); }
        return new(true);
    }

    /// <summary>Undoes a manual adjustment; the derived PR from the sets applies again.</summary>
    public async Task<bool> RemovePrAdjustmentAsync(int userId, int exerciseId, DateOnly date)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var deleted = await db.PrAdjustments
            .Where(a => a.UserId == userId && a.ExerciseId == exerciseId && a.Date == date)
            .ExecuteDeleteAsync();
        return deleted > 0;
    }

    /// <summary>How this user types the weight for this exercise; the total when never changed.</summary>
    public async Task<BarSetting> GetBarSettingAsync(int userId, int exerciseId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.ExerciseBarSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ExerciseId == exerciseId);
        return row is null ? BarSetting.Total : new BarSetting(row.PerSide, row.BarKg);
    }

    /// <summary>Remembers Total / Per side and the bar weight for this exercise. Only changes how the weight is
    /// typed; the logged sets keep their totals.</summary>
    public async Task<SetResult> SaveBarSettingAsync(int userId, int exerciseId, BarSetting setting)
    {
        if (setting.BarKg is { } bar && (!double.IsFinite(bar) || bar < 0 || bar > MaxBarKg))
            return new(false, $"The bar must be 0 to {MaxBarKg:0} kg.", FormField.Weight);

        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await ExerciseAvailableAsync(db, userId, exerciseId)) return new(false, "Exercise not found.");

        var row = await db.ExerciseBarSettings.FirstOrDefaultAsync(x => x.UserId == userId && x.ExerciseId == exerciseId);
        if (row is null)
            db.ExerciseBarSettings.Add(row = new ExerciseBarSetting { UserId = userId, ExerciseId = exerciseId });
        row.PerSide = setting.PerSide;
        row.BarKg = setting.BarKg is { } kg ? Math.Round(kg, 2) : null;

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "Could not save the bar weight."); }
        return new(true);
    }

    private static SetEntry ToEntry(SetLog s) => new(s.Date, s.SetNumber, s.Kind, s.Reps, s.WeightKg);

    private static Task<bool> ExerciseAvailableAsync(AppDbContext db, int userId, int exerciseId) =>
        db.Exercises.AnyAsync(e => e.Id == exerciseId && (e.OwnerUserId == null || e.OwnerUserId == userId));

    private static (string? Error, string? Field) ValidateSet(int reps, double weightKg)
    {
        if (reps < 1 || reps > MaxReps) return ($"Reps must be 1 to {MaxReps}.", FormField.Reps);
        if (!double.IsFinite(weightKg) || weightKg < 0 || weightKg > MaxWeightKg)
            return ($"Weight must be 0 to {MaxWeightKg:0} kg.", FormField.Weight);
        return (null, null);
    }
}