using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>One card on the Workouts menu. Weekdays is "Mon · Fri" (Monday-first), empty if the day is not
/// scheduled in the current week.</summary>
public record MenuDay(int SplitDayId, string Name, string Weekdays, int ExerciseCount, bool IsToday);

public record WorkoutMenu(string SplitName, bool IsRestToday, IReadOnlyList<MenuDay> Days);

public class WorkoutMenuService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>The days of the user's selected split, or null when no split is selected or it has no usable schedule.</summary>
    public async Task<WorkoutMenu?> GetAsync(int userId, DateOnly? today = null)
    {
        var date = today ?? DateOnly.FromDateTime(DateTime.Now);
        await using var db = await dbFactory.CreateDbContextAsync();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.SelectedSplitId is not { } splitId) return null;

        var split = await db.WorkoutSplits.AsNoTracking()
            .Include(s => s.Days).Include(s => s.Variants)
            .FirstOrDefaultAsync(s => s.Id == splitId);
        var resolved = split is null ? null : SplitSchedule.Resolve(split, user);
        if (split is null || resolved is null) return null;

        var ids = resolved.Days.Select(d => d.Id).ToList();
        var counts = await db.UserDayExercises.AsNoTracking()
            .Where(x => x.UserId == userId && ids.Contains(x.SplitDayId))
            .GroupBy(x => x.SplitDayId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);

        var count = resolved.Days.Count;
        var todayWorkout = TodayPlan.Resolve(resolved.Week, resolved.Days.Select(d => d.Name).ToList(), date);
        int? todayDayId = todayWorkout.RotationIndex is { } t ? resolved.Days[t % count].Id : null;

        var days = resolved.Days.Select((d, index) => new MenuDay(
            d.Id,
            d.Name,
            string.Join(" · ", resolved.Week
                .Where(s => s.RotationIndex is { } r && r % count == index)
                .Select(s => s.Weekday.ToString()[..3])),
            counts.GetValueOrDefault(d.Id),
            d.Id == todayDayId)).ToList();

        return new WorkoutMenu(split.Name, !todayWorkout.IsTraining, days);
    }
}