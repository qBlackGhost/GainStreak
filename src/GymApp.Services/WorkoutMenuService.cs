using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>One exercise on a Workouts card, with its planned sets.</summary>
public record MenuExercise(string Name, int WarmupSets, int WorkingSets);

/// <summary>One card on the Workouts menu. Weekdays is "Mon · Fri" (Monday-first), empty if the day is not
/// scheduled in the current week. <see cref="Exercises"/> are the user's list for the day, in order.
/// <see cref="IsNext"/>: the first training day after today.</summary>
public record MenuDay(int SplitDayId, string Name, string Weekdays, int ExerciseCount, bool IsToday,
    IReadOnlyList<MenuExercise> Exercises, bool IsNext = false);

/// <summary>One day of the Workouts week strip: the planned day's name, or "Rest".</summary>
public record MenuWeekday(DateOnly Date, string Name, bool IsTraining, bool IsToday);

/// <summary><see cref="Week"/> is Monday to Sunday of the current week, as planned (missed-day choices aside).</summary>
public record WorkoutMenu(string SplitName, bool IsRestToday, IReadOnlyList<MenuDay> Days, IReadOnlyList<MenuWeekday> Week);

public class WorkoutMenuService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>The days of the user's selected split, or null when no split is selected or it has no usable schedule.</summary>
    public async Task<WorkoutMenu?> GetAsync(int userId, DateOnly? today = null)
    {
        var date = today ?? AppClock.Today;
        await using var db = await dbFactory.CreateDbContextAsync();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.SelectedSplitId is not { } splitId) return null;

        var split = await db.WorkoutSplits.AsNoTracking()
            .Include(s => s.Days).Include(s => s.Variants)
            .FirstOrDefaultAsync(s => s.Id == splitId);
        var resolved = split is null ? null : SplitSchedule.Resolve(split, user);
        if (split is null || resolved is null) return null;

        var ids = resolved.Days.Select(d => d.Id).ToList();
        var lists = (await db.UserDayExercises.AsNoTracking()
                .Where(x => x.UserId == userId && ids.Contains(x.SplitDayId))
                .OrderBy(x => x.Order)
                .Select(x => new { x.SplitDayId, x.Exercise.Name, x.WarmupSets, x.WorkingSets })
                .ToListAsync())
            .ToLookup(x => x.SplitDayId, x => new MenuExercise(x.Name, x.WarmupSets, x.WorkingSets));

        var count = resolved.Days.Count;
        var names = resolved.Days.Select(d => d.Name).ToList();
        var todayWorkout = TodayPlan.Resolve(resolved.Week, names, date);
        int? todayDayId = todayWorkout.RotationIndex is { } t ? resolved.Days[t % count].Id : null;

        // The next training day after today (a week ahead at most)
        int? nextDayId = Enumerable.Range(1, 7)
            .Select(k => TodayPlan.Resolve(resolved.Week, names, date.AddDays(k)))
            .FirstOrDefault(w => w.IsTraining)?.RotationIndex is { } n ? resolved.Days[n % count].Id : null;

        var monday = PointsCalculator.MondayOf(date);
        var week = Enumerable.Range(0, 7).Select(i =>
        {
            var d = monday.AddDays(i);
            var w = TodayPlan.Resolve(resolved.Week, names, d);
            return new MenuWeekday(d, w.IsTraining ? w.Title : "Rest", w.IsTraining, d == date);
        }).ToList();

        var days = resolved.Days.Select((d, index) => new MenuDay(
            d.Id,
            d.Name,
            string.Join(" · ", resolved.Week
                .Where(s => s.RotationIndex is { } r && r % count == index)
                .Select(s => s.Weekday.ToString()[..3])),
            lists[d.Id].Count(),
            d.Id == todayDayId,
            lists[d.Id].ToList(),
            d.Id == nextDayId)).ToList();

        return new WorkoutMenu(split.Name, !todayWorkout.IsTraining, days, week);
    }
}