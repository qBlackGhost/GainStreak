using System;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Desktop.Data;
using GymApp.Desktop.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Desktop.Services;

public class StreakService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// Recomputes the streak from the logs and caches it on the user. Call it after every workout log
    /// change, after a schedule change, and when the app starts or a user signs in, because a streak can
    /// break at midnight without anything being logged.
    /// </summary>
    public async Task<StreakResult> RecalculateAsync(int userId, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Now);
        await using var db = await dbFactory.CreateDbContextAsync();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return new StreakResult(0, 0, StreakStatus.NotStarted);

        var dates = await db.WorkoutLogs.Where(w => w.UserId == userId).Select(w => w.Date).ToListAsync();
        var schedule = await db.UserSchedules.Where(s => s.UserId == userId)
            .OrderBy(s => s.EffectiveFrom)
            .Select(s => new ScheduleChange(s.EffectiveFrom, s.WeekPattern))
            .ToListAsync();

        // Users who picked a split before schedule history existed: create their first entry from the
        // current selection. The first entry also covers earlier days, so nothing else is needed.
        if (schedule.Count == 0 && user.SelectedSplitId is { } splitId)
        {
            var pattern = await PatternFromSelectionAsync(db, user, splitId);
            if (pattern is not null)
            {
                db.UserSchedules.Add(new UserSchedule
                    { UserId = userId, EffectiveFrom = day, WeekPattern = pattern, SplitId = splitId });
                schedule.Add(new ScheduleChange(day, pattern));
            }
        }

        var result = StreakCalculator.Calculate(dates, schedule, day);

        user.CurrentStreak = result.Current;
        user.LongestStreak = Math.Max(user.LongestStreak, result.Longest);   // a personal best never shrinks
        await db.SaveChangesAsync();
        return result;
    }

    private static async Task<string?> PatternFromSelectionAsync(AppDbContext db, User user, int splitId)
    {
        var split = await db.WorkoutSplits.AsNoTracking()
            .Include(s => s.Days).Include(s => s.Variants)
            .FirstOrDefaultAsync(s => s.Id == splitId);
        var resolved = split is null ? null : SplitSchedule.Resolve(split, user);
        return resolved is null ? null : WeekPattern.From(resolved.Week);
    }
}
