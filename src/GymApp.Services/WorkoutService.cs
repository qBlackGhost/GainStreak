using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>What Home shows for today. Title is empty when the user has no split.</summary>
public record TodayInfo(DateOnly Date, bool HasSplit, bool IsTraining, string Title, bool Logged);

public record LogResult(bool Success, string? Error = null, StreakResult? Streak = null, PointsSync? Points = null);

/// <summary>A split day that can be checked in.</summary>
public record CheckInDay(int SplitDayId, string Name);

/// <summary>One exercise in the check-in popup, with the sets already logged for it today.</summary>
public record CheckInExerciseInfo(int ExerciseId, string Name, int WarmupSets, int WorkingSets, IReadOnlyList<SetEntry> TodaySets);

/// <summary>
/// What the check-in popup shows. <see cref="PlannedDay"/> is null on a rest day or with no split; then
/// <see cref="PickableDays"/> lists the split's days that have exercises. <see cref="Day"/> is the day the
/// <see cref="Exercises"/> and <see cref="Status"/> are for (null when there's nothing to check in).
/// </summary>
public record CheckInInfo(DateOnly Date, bool HasSplit, bool Logged, CheckInDay? PlannedDay,
    IReadOnlyList<CheckInDay> PickableDays, CheckInDay? Day, IReadOnlyList<CheckInExerciseInfo> Exercises, CheckInStatus Status);

public class WorkoutService(IDbContextFactory<AppDbContext> dbFactory, StreakService streaks, PointsService points)
{
    private record Plan(int SplitId, TodayWorkout Workout, SplitDay? Day);

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
    /// What the check-in popup needs: today's planned day (null on a rest day or with no split), the exercises of the
    /// day being checked in with the sets already logged today, and the <see cref="CheckInRule"/> status.
    /// On a rest day the user picks the day they trained from <see cref="CheckInInfo.PickableDays"/>;
    /// <paramref name="splitDayId"/> is that pick (default: the day already logged today, else the first pickable day).
    /// </summary>
    public async Task<CheckInInfo> GetCheckInAsync(int userId, int? splitDayId = null, DateOnly? today = null)
    {
        var date = today ?? Today();
        await using var db = await dbFactory.CreateDbContextAsync();

        var log = await db.WorkoutLogs.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == userId && w.Date == date);
        var plan = await PlanAsync(db, userId, date);
        var planned = plan?.Day is { } p ? new CheckInDay(p.Id, p.Name) : null;

        IReadOnlyList<CheckInDay> pickable = [];
        var day = planned;
        if (plan is not null && planned is null)
        {
            pickable = await PickableDaysAsync(db, userId);
            day = pickable.FirstOrDefault(d => d.SplitDayId == splitDayId)
                  ?? pickable.FirstOrDefault(d => d.SplitDayId == log?.SplitDayId)
                  ?? pickable.FirstOrDefault();
        }

        var exercises = day is null ? [] : await ExercisesAsync(db, userId, day.SplitDayId, date);
        return new CheckInInfo(date, plan is not null, log is not null, planned, pickable, day, exercises, Status(exercises));
    }

    /// <summary>
    /// Logs a workout for today, and only today (no backdating). The day must pass <see cref="CheckInRule"/> with
    /// the sets already saved today, so save them first (<see cref="SetService.SaveDayAsync"/>). On a training day
    /// the planned day is used; <paramref name="splitDayId"/> is only for rest days, where the user picks one of
    /// their split's days. Tapping twice is harmless: the unique (user, date) index keeps one log per day, and the
    /// second call just returns the current streak.
    /// </summary>
    public async Task<LogResult> LogTodayAsync(int userId, int? splitDayId = null, DateOnly? today = null)
    {
        var date = today ?? Today();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            if (!await db.WorkoutLogs.AnyAsync(w => w.UserId == userId && w.Date == date))
            {
                var plan = await PlanAsync(db, userId, date);
                if (plan is null) return new LogResult(false, "Pick a split and build a workout first.");

                CheckInDay? day = plan.Day is { } p ? new CheckInDay(p.Id, p.Name) : null;
                if (day is null)
                {
                    if (splitDayId is not { } picked) return new LogResult(false, "Pick the day you trained.");
                    day = await db.SplitDays.Where(d => d.SplitId == plan.SplitId && d.Id == picked)
                        .Select(d => new CheckInDay(d.Id, d.Name)).FirstOrDefaultAsync();
                    if (day is null) return new LogResult(false, "That day isn't in your split.");
                }

                var exercises = await ExercisesAsync(db, userId, day.SplitDayId, date);
                var status = Status(exercises);
                if (status.Total == 0) return new LogResult(false, $"Build a workout for {day.Name} first.");
                if (!status.CanLog)
                    return new LogResult(false,
                        $"Fill in at least {status.Required} of {status.Total} exercises to log this workout.");

                db.WorkoutLogs.Add(new WorkoutLog { UserId = userId, Date = date, SplitDayId = day.SplitDayId });
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Most likely the unique (user, date) index: another tap or window already logged today
                    db.ChangeTracker.Clear();
                    if (!await db.WorkoutLogs.AnyAsync(w => w.UserId == userId && w.Date == date))
                        return new LogResult(false, "Could not save your workout.");
                }
            }
        }

        return await RefreshAsync(userId, date);
    }

    /// <summary>
    /// Undoes today's check-in, and only today's: deletes the log (and with it today's gym choice), then
    /// recalculates the streak and points, so the +5 and any week bonus it earned go away. If today's streak was the
    /// best one, the best streak goes back too. Today's sets stay.
    /// Nothing logged is harmless, like a double tap.
    /// </summary>
    public async Task<LogResult> UndoTodayAsync(int userId, DateOnly? today = null)
    {
        var date = today ?? Today();
        int deleted;
        await using (var db = await dbFactory.CreateDbContextAsync())
            deleted = await db.WorkoutLogs.Where(w => w.UserId == userId && w.Date == date).ExecuteDeleteAsync();

        return await RefreshAsync(userId, date, undoneToday: deleted > 0);
    }

    private async Task<LogResult> RefreshAsync(int userId, DateOnly date, bool undoneToday = false)
    {
        var streak = await streaks.RecalculateAsync(userId, date, undoneToday);
        return new LogResult(true, Streak: streak, Points: await points.SyncAsync(userId, date));
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    private static CheckInStatus Status(IReadOnlyList<CheckInExerciseInfo> exercises) =>
        CheckInRule.Evaluate(exercises.Select(e => new CheckInExercise(e.ExerciseId, e.WorkingSets, e.TodaySets)));

    private static async Task<IReadOnlyList<CheckInExerciseInfo>> ExercisesAsync(
        AppDbContext db, int userId, int splitDayId, DateOnly date)
    {
        var rows = await db.UserDayExercises.AsNoTracking()
            .Where(x => x.UserId == userId && x.SplitDayId == splitDayId)
            .OrderBy(x => x.Order)
            .Select(x => new { x.ExerciseId, x.Exercise.Name, x.WarmupSets, x.WorkingSets })
            .ToListAsync();
        var ids = rows.Select(r => r.ExerciseId).ToList();
        var sets = await db.SetLogs.AsNoTracking()
            .Where(s => s.UserId == userId && s.Date == date && ids.Contains(s.ExerciseId))
            .OrderBy(s => s.SetNumber)
            .ToListAsync();
        var byExercise = sets.ToLookup(s => s.ExerciseId, s => new SetEntry(s.Date, s.SetNumber, s.Kind, s.Reps, s.WeightKg));

        return rows.Select(r => new CheckInExerciseInfo(r.ExerciseId, r.Name, r.WarmupSets, r.WorkingSets,
            byExercise[r.ExerciseId].ToList())).ToList();
    }

    /// <summary>The selected split's days that have at least one exercise, for the rest-day picker.</summary>
    private static async Task<IReadOnlyList<CheckInDay>> PickableDaysAsync(AppDbContext db, int userId)
    {
        var splitId = await db.Users.Where(u => u.Id == userId).Select(u => u.SelectedSplitId).FirstOrDefaultAsync();
        if (splitId is not { } id) return [];
        return await db.SplitDays.AsNoTracking()
            .Where(d => d.SplitId == id && db.UserDayExercises.Any(x => x.UserId == userId && x.SplitDayId == d.Id))
            .OrderBy(d => d.Order)
            .Select(d => new CheckInDay(d.Id, d.Name))
            .ToListAsync();
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
        return new Plan(splitId, workout, day);
    }
}
