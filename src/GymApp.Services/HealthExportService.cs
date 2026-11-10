using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>
/// Phase 20: what Apple Health should hold for a window of days, built from the app's own records only (decision 4):
/// <list type="bullet">
/// <item>a strength workout for each day whose workout counted (a <c>WorkoutLog</c>) and whose live session ended,
/// from its start to its end, titled with the day name;</item>
/// <item>a cardio workout for each finished cardio session that saved minutes, as long as the saved minutes;</item>
/// <item>each weigh-in typed in the app, at 08:00 that day (the app keeps the day, not the time); never one imported
/// from Health.</item>
/// </list>
/// Read-only; <c>HealthSync</c> compares this with Health and applies the difference.
/// </summary>
public class HealthExportService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>The weigh-in's time in Health: the app keeps only the day.</summary>
    public static readonly TimeOnly WeighInTime = new(8, 0);

    public async Task<IReadOnlyList<HealthItem>> WantedAsync(int userId, DateOnly from, DateOnly to)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var items = new List<HealthItem>();

        var logged = await db.WorkoutLogs.AsNoTracking()
            .Where(w => w.UserId == userId && w.Date >= from && w.Date <= to)
            .Select(w => w.Date).ToListAsync();
        var sessions = await db.LiveWorkouts.AsNoTracking()
            .Where(l => l.UserId == userId && l.Date >= from && l.Date <= to && l.EndedAt != null)
            .Select(l => new { l.Date, l.StartedAt, EndedAt = l.EndedAt!.Value, Day = l.SplitDay == null ? null : l.SplitDay.Name })
            .ToListAsync();
        foreach (var date in logged.Distinct())
        {
            // A day can have a session that ended unlogged and a later one that counted: the last one is the workout
            if (sessions.Where(s => s.Date == date && s.EndedAt > s.StartedAt).MaxBy(s => s.StartedAt) is not { } s) continue;
            items.Add(new HealthItem(HealthPlan.WorkoutId(userId, date), HealthKind.StrengthWorkout, s.StartedAt, s.EndedAt,
                Title: s.Day ?? LiveWorkoutService.PickedName));
        }

        var cardio = await db.CardioSessions.AsNoTracking()
            .Where(c => c.UserId == userId && c.Date >= from && c.Date <= to && c.EndedAt != null && c.SavedMinutes > 0)
            .Select(c => new { c.Id, c.Date, c.StartedAt, Minutes = c.SavedMinutes!.Value })
            .ToListAsync();
        items.AddRange(cardio.Select(c => new HealthItem(HealthPlan.CardioId(userId, c.Date, c.Id), HealthKind.CardioWorkout,
            c.StartedAt, c.StartedAt.AddMinutes(c.Minutes), Title: "Cardio")));

        var weights = await db.WeightLogs.AsNoTracking()
            .Where(w => w.UserId == userId && w.Date >= from && w.Date <= to && !w.FromHealth)   // never back to Health
            .Select(w => new { w.Date, w.WeightKg }).ToListAsync();
        items.AddRange(weights.Select(w =>
        {
            var at = w.Date.ToDateTime(WeighInTime);
            return new HealthItem(HealthPlan.WeightId(userId, w.Date), HealthKind.Weight, at, at, w.WeightKg);
        }));
        return items;
    }
}
