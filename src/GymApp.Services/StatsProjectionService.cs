using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>Loads a user's logs and ledger for <see cref="StatsProjection"/>. Only reads.</summary>
public class StatsProjectionService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>The projected stats from <paramref name="today"/> on, or null if the user doesn't exist.
    /// Call it after the points sync, so the ledger it starts from is current.</summary>
    public async Task<IReadOnlyList<ProjectedStats>?> ProjectAsync(int userId, DateOnly? today = null, int days = StatsProjection.DefaultDays)
    {
        var day = today ?? AppClock.Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Users.AnyAsync(u => u.Id == userId)) return null;

        var windowStart = PointsService.WindowStart(day);
        var workoutDates = await db.WorkoutLogs.Where(w => w.UserId == userId).Select(w => w.Date).ToListAsync();
        var schedule = await db.UserSchedules.Where(s => s.UserId == userId)
            .OrderBy(s => s.EffectiveFrom)
            .Select(s => new ScheduleChange(s.EffectiveFrom, s.WeekPattern))
            .ToListAsync();
        var choices = await StreakService.LoadChoicesAsync(db, userId);
        var calories = await db.CalorieLogs.Where(c => c.UserId == userId && c.Date >= windowStart)
            .Select(c => new CalorieRecord(c.Date, c.CaloriesConsumed, c.GoalAtTime))
            .ToListAsync();
        var ledger = await db.PointsEntries.Where(p => p.UserId == userId)
            .Select(p => new PointsAward(p.Reason, p.Amount, p.Date, p.DedupKey))
            .ToListAsync();

        return StatsProjection.Project(workoutDates, schedule, choices, calories, ledger, day, days);
    }
}
