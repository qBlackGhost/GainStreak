using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

public record ExerciseResult(bool Success, string? Error = null, Exercise? Exercise = null, string? Field = null);

/// <summary>How much of the user's data points at an exercise. Deleting it removes all of this too.</summary>
public record ExerciseUsage(int DayLists, int Sets);

public class ExerciseService(IDbContextFactory<AppDbContext> dbFactory)
{
    public const int MaxCustomExercises = 30;
    public const int MaxNameLength = 60;

    /// <summary>Built-ins plus the user's own, grouped by muscle group (enum order), then by name.
    /// The search matches any part of the name, ignoring case.</summary>
    public async Task<List<Exercise>> GetAvailableAsync(int userId, string? search = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var all = await db.Exercises.AsNoTracking()
            .Where(e => e.OwnerUserId == null || e.OwnerUserId == userId)
            .ToListAsync();

        // The catalog is small, so filtering and sorting in memory is simpler than translating it to SQL
        // (and the enum is stored as text, so a SQL ORDER BY would sort the groups alphabetically)
        var term = search?.Trim();
        return all
            .Where(e => string.IsNullOrEmpty(term) || e.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.MuscleGroup)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<Exercise>> GetCustomAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Exercises.AsNoTracking()
            .Where(e => e.OwnerUserId == userId)
            .OrderBy(e => e.Id)
            .ToListAsync();
    }

    public async Task<ExerciseResult> CreateAsync(int userId, string name, MuscleGroup group)
    {
        if (Validate(name, group) is ({ } error, var field)) return new(false, error, Field: field);
        name = name.Trim();

        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Exercises.CountAsync(e => e.OwnerUserId == userId) >= MaxCustomExercises)
            return new(false, $"You can have at most {MaxCustomExercises} custom exercises.");
        if (await NameTakenAsync(db, userId, name, exceptId: 0))
            return new(false, "An exercise with that name already exists.", Field: FormField.Name);

        var exercise = new Exercise { Name = name, MuscleGroup = group, OwnerUserId = userId };
        db.Exercises.Add(exercise);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "An exercise with that name already exists.", Field: FormField.Name); }

        return new(true, Exercise: exercise);
    }

    /// <summary>Only the user's own exercises can be edited, never built-ins.</summary>
    public async Task<ExerciseResult> UpdateAsync(int userId, int exerciseId, string name, MuscleGroup group)
    {
        if (Validate(name, group) is ({ } error, var field)) return new(false, error, Field: field);
        name = name.Trim();

        await using var db = await dbFactory.CreateDbContextAsync();
        var exercise = await db.Exercises.FirstOrDefaultAsync(e => e.Id == exerciseId && e.OwnerUserId == userId);
        if (exercise is null) return new(false, "Exercise not found.");
        if (await NameTakenAsync(db, userId, name, exerciseId))
            return new(false, "An exercise with that name already exists.", Field: FormField.Name);

        exercise.Name = name;
        exercise.MuscleGroup = group;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(false, "An exercise with that name already exists.", Field: FormField.Name); }

        return new(true, Exercise: exercise);
    }

    /// <summary>Use before deleting, so the UI can warn about what will go with it.</summary>
    public async Task<ExerciseUsage> GetUsageAsync(int userId, int exerciseId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return new ExerciseUsage(
            await db.UserDayExercises.CountAsync(x => x.UserId == userId && x.ExerciseId == exerciseId),
            await db.SetLogs.CountAsync(s => s.UserId == userId && s.ExerciseId == exerciseId));
    }

    /// <summary>Deletes one of the user's own exercises. Its day-list entries, sets and PR adjustments go
    /// with it (cascade). Built-ins and other users' exercises are never touched.</summary>
    public async Task<bool> DeleteAsync(int userId, int exerciseId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var deleted = await db.Exercises
            .Where(e => e.Id == exerciseId && e.OwnerUserId == userId)
            .ExecuteDeleteAsync();
        return deleted > 0;
    }

    // The Name column is NOCASE, so == is already case-insensitive in SQLite.
    // A name clashes with the built-ins and with the user's own exercises, but not with other users'.
    private static Task<bool> NameTakenAsync(AppDbContext db, int userId, string name, int exceptId) =>
        db.Exercises.AnyAsync(e =>
            (e.OwnerUserId == null || e.OwnerUserId == userId) && e.Id != exceptId && e.Name == name);

    private static (string? Error, string? Field) Validate(string name, MuscleGroup group)
    {
        if (string.IsNullOrWhiteSpace(name)) return ("Give your exercise a name.", FormField.Name);
        if (name.Trim().Length > MaxNameLength) return ($"Exercise name must be at most {MaxNameLength} characters.", FormField.Name);
        if (!Enum.IsDefined(group)) return ("Pick a muscle group.", FormField.MuscleGroup);
        return (null, null);
    }
}