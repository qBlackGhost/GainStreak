using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

public enum CopyMode { Append, Replace }

public record DayListResult(bool Success, string? Error = null);
public record CopyResult(bool Success, string? Error = null, int Added = 0, int Skipped = 0);

/// <summary>Another day the user has exercises for, offered as a copy source.</summary>
public record CopySource(int SplitDayId, string DayName, string SplitName, int ExerciseCount);

public class DayExerciseService(IDbContextFactory<AppDbContext> dbFactory)
{
    public const int MaxExercisesPerDay = 20;
    public const int MaxWarmupSets = 10;
    public const int MaxWorkingSets = 15;

    public async Task<List<UserDayExercise>> GetListAsync(int userId, int splitDayId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.UserDayExercises.AsNoTracking()
            .Include(x => x.Exercise)
            .Where(x => x.UserId == userId && x.SplitDayId == splitDayId)
            .OrderBy(x => x.Order)
            .ToListAsync();
    }

    public async Task<DayListResult> AddAsync(
        int userId, int splitDayId, int exerciseId, int warmupSets = 0, int workingSets = 3)
    {
        if (ValidateSets(warmupSets, workingSets) is { } setError) return new(false, setError);

        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await DayAvailableAsync(db, userId, splitDayId)) return new(false, "Day not found.");
        if (!await db.Exercises.AnyAsync(e =>
                e.Id == exerciseId && (e.OwnerUserId == null || e.OwnerUserId == userId)))
            return new(false, "Exercise not found.");

        var existing = await db.UserDayExercises
            .Where(x => x.UserId == userId && x.SplitDayId == splitDayId)
            .Select(x => new { x.ExerciseId, x.Order })
            .ToListAsync();
        if (existing.Any(x => x.ExerciseId == exerciseId)) return new(false, "That exercise is already in this day.");
        if (existing.Count >= MaxExercisesPerDay)
            return new(false, $"A day can have at most {MaxExercisesPerDay} exercises.");

        db.UserDayExercises.Add(new UserDayExercise
        {
            UserId = userId, SplitDayId = splitDayId, ExerciseId = exerciseId,
            Order = existing.Count == 0 ? 1 : existing.Max(x => x.Order) + 1,
            WarmupSets = warmupSets, WorkingSets = workingSets
        });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "That exercise is already in this day."); }
        return new(true);
    }

    /// <summary>Removes the exercise from this day's list only. Logged sets and PRs belong to the
    /// exercise, not the day, so they stay.</summary>
    public async Task<bool> RemoveAsync(int userId, int splitDayId, int exerciseId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await LoadOrderedAsync(db, userId, splitDayId);
        var target = rows.FirstOrDefault(x => x.ExerciseId == exerciseId);
        if (target is null) return false;

        db.UserDayExercises.Remove(target);
        rows.Remove(target);
        Renumber(rows);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>direction -1 = up, +1 = down. Returns false at the edges or if the exercise is not in the list.</summary>
    public async Task<bool> MoveAsync(int userId, int splitDayId, int exerciseId, int direction)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await LoadOrderedAsync(db, userId, splitDayId);
        var i = rows.FindIndex(x => x.ExerciseId == exerciseId);
        var j = i + Math.Sign(direction);
        if (i < 0 || j < 0 || j >= rows.Count) return false;

        (rows[i], rows[j]) = (rows[j], rows[i]);
        Renumber(rows);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Moves an exercise to <paramref name="index"/> (0-based, clamped to the list), shifting the others,
    /// as dragging it there does. Returns false if the exercise is not in the list.</summary>
    public async Task<bool> MoveToAsync(int userId, int splitDayId, int exerciseId, int index)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await LoadOrderedAsync(db, userId, splitDayId);
        var i = rows.FindIndex(x => x.ExerciseId == exerciseId);
        if (i < 0) return false;

        var row = rows[i];
        rows.RemoveAt(i);
        rows.Insert(Math.Clamp(index, 0, rows.Count), row);
        Renumber(rows);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<DayListResult> SetPlannedSetsAsync(
        int userId, int splitDayId, int exerciseId, int warmupSets, int workingSets)
    {
        if (ValidateSets(warmupSets, workingSets) is { } error) return new(false, error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.UserDayExercises.FirstOrDefaultAsync(x =>
            x.UserId == userId && x.SplitDayId == splitDayId && x.ExerciseId == exerciseId);
        if (row is null) return new(false, "That exercise is not in this day.");

        row.WarmupSets = warmupSets;
        row.WorkingSets = workingSets;
        await db.SaveChangesAsync();
        return new(true);
    }

    /// <summary>The user's other days that have exercises, from any split (presets first).</summary>
    public async Task<List<CopySource>> GetCopySourcesAsync(int userId, int splitDayId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var counts = await db.UserDayExercises.AsNoTracking()
            .Where(x => x.UserId == userId && x.SplitDayId != splitDayId)
            .GroupBy(x => x.SplitDayId)
            .Select(g => new { DayId = g.Key, Count = g.Count() })
            .ToListAsync();
        if (counts.Count == 0) return [];

        var ids = counts.Select(c => c.DayId).ToList();
        var days = await db.SplitDays.AsNoTracking().Include(d => d.Split)
            .Where(d => ids.Contains(d.Id))
            .ToListAsync();

        return days.OrderBy(d => d.SplitId).ThenBy(d => d.Order)
            .Select(d => new CopySource(d.Id, d.Name, d.Split.Name, counts.First(c => c.DayId == d.Id).Count))
            .ToList();
    }

    /// <summary>
    /// Copies the exercise list and planned set counts from one of the user's days into another.
    /// Append adds the missing ones to the end and skips exercises already there; Replace swaps the whole
    /// list. Logged sets and PRs are never copied or touched. All or nothing.
    /// </summary>
    public async Task<CopyResult> CopyAsync(int userId, int fromDayId, int toDayId, CopyMode mode)
    {
        if (fromDayId == toDayId) return new(false, "Pick a different day to copy from.");

        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await DayAvailableAsync(db, userId, toDayId)) return new(false, "Day not found.");

        var source = await db.UserDayExercises.AsNoTracking()
            .Where(x => x.UserId == userId && x.SplitDayId == fromDayId)
            .OrderBy(x => x.Order)
            .ToListAsync();
        if (source.Count == 0) return new(false, "That day has no exercises to copy.");

        var target = await db.UserDayExercises.AsNoTracking()
            .Where(x => x.UserId == userId && x.SplitDayId == toDayId)
            .OrderBy(x => x.Order)
            .ToListAsync();

        var keep = mode == CopyMode.Replace ? new List<UserDayExercise>() : target;
        var have = keep.Select(x => x.ExerciseId).ToHashSet();
        var toAdd = source.Where(s => !have.Contains(s.ExerciseId)).ToList();

        if (keep.Count + toAdd.Count > MaxExercisesPerDay)
            return new(false, $"That would make more than {MaxExercisesPerDay} exercises on this day.");

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            if (mode == CopyMode.Replace && target.Count > 0)
                await db.UserDayExercises
                    .Where(x => x.UserId == userId && x.SplitDayId == toDayId)
                    .ExecuteDeleteAsync();

            var order = keep.Count == 0 ? 0 : keep.Max(x => x.Order);
            foreach (var s in toAdd)
                db.UserDayExercises.Add(new UserDayExercise
                {
                    UserId = userId, SplitDayId = toDayId, ExerciseId = s.ExerciseId, Order = ++order,
                    WarmupSets = s.WarmupSets, WorkingSets = s.WorkingSets
                });

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            return new(false, "Could not copy the exercises.");
        }

        return new(true, Added: toAdd.Count, Skipped: source.Count - toAdd.Count);
    }

    // A day is usable if it belongs to a preset or to one of the user's own custom splits
    private static Task<bool> DayAvailableAsync(AppDbContext db, int userId, int splitDayId) =>
        db.SplitDays.AnyAsync(d =>
            d.Id == splitDayId && (d.Split.OwnerUserId == null || d.Split.OwnerUserId == userId));

    private static Task<List<UserDayExercise>> LoadOrderedAsync(AppDbContext db, int userId, int splitDayId) =>
        db.UserDayExercises
            .Where(x => x.UserId == userId && x.SplitDayId == splitDayId)
            .OrderBy(x => x.Order)
            .ToListAsync();

    private static void Renumber(List<UserDayExercise> rows)
    {
        for (var i = 0; i < rows.Count; i++) rows[i].Order = i + 1;
    }

    private static string? ValidateSets(int warmupSets, int workingSets)
    {
        if (warmupSets < 0 || warmupSets > MaxWarmupSets) return $"Warmup sets must be 0 to {MaxWarmupSets}.";
        if (workingSets < 1 || workingSets > MaxWorkingSets) return $"Working sets must be 1 to {MaxWorkingSets}.";
        return null;
    }
}