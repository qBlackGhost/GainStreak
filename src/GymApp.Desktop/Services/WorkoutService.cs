using System;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Desktop.Data;
using GymApp.Desktop.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Desktop.Services;

/// <summary>What Home shows for today. Title is empty when the user has no split.</summary>
public record TodayInfo(DateOnly Date, bool HasSplit, bool IsTraining, string Title, bool Logged);

public record LogResult(bool Success, string? Error = null, StreakResult? Streak = null);

public class WorkoutService(IDbContextFactory<AppDbContext> dbFactory, StreakService streaks)
{
    private record Plan(TodayWorkout Workout, SplitDay? Day);

    public async Task<TodayInfo> GetTodayAsync(int userId)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        await using var db = await dbFactory.CreateDbContextAsync();

        var logged = await db.WorkoutLogs.AnyAsync(w => w.UserId == userId && w.Date == today);
        var plan = await PlanAsync(db, userId, today);

        return plan is null
            ? new TodayInfo(today, false, true, "", logged)
            : new TodayInfo(today, true, plan.Workout.IsTraining, plan.Workout.Title, logged);
    }

    /// <summary>
    /// Logs a workout for today, and only today (no backdating). Tapping twice is harmless: the unique
    /// (user, date) index keeps one log per day, and the second call just returns the current streak.
    /// </summary>
    public async Task<LogResult> LogTodayAsync(int userId)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            if (!await db.WorkoutLogs.AnyAsync(w => w.UserId == userId && w.Date == today))
            {
                var plan = await PlanAsync(db, userId, today);
                db.WorkoutLogs.Add(new WorkoutLog { UserId = userId, Date = today, SplitDayId = plan?.Day?.Id });
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Most likely the unique (user, date) index: another tap or window already logged today
                    db.ChangeTracker.Clear();
                    if (!await db.WorkoutLogs.AnyAsync(w => w.UserId == userId && w.Date == today))
                        return new LogResult(false, "Could not save your workout.");
                }
            }
        }

        return new LogResult(true, Streak: await streaks.RecalculateAsync(userId));
    }

    private static async Task<Plan?> PlanAsync(AppDbContext db, int userId, DateOnly date)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || user.SelectedSplitId is not { } splitId) return null;

        var split = await db.WorkoutSplits.AsNoTracking()
            .Include(s => s.Days).Include(s => s.Variants)
            .FirstOrDefaultAsync(s => s.Id == splitId);
        var resolved = split is null ? null : SplitSchedule.Resolve(split, user);
        if (resolved is null) return null;

        var workout = TodayPlan.Resolve(resolved.Week, resolved.Days.Select(d => d.Name).ToList(), date);
        var day = workout.RotationIndex is { } i ? resolved.Days[i % resolved.Days.Count] : null;
        return new Plan(workout, day);
    }
}
