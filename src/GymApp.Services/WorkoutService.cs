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

/// <summary>One exercise in the check-in popup, with the sets already logged for it today and the sets of the most
/// recent earlier session (empty when there is none).</summary>
public record CheckInExerciseInfo(int ExerciseId, string Name, int WarmupSets, int WorkingSets, IReadOnlyList<SetEntry> TodaySets,
    IReadOnlyList<SetEntry> LastSession);

/// <summary>
/// What the check-in popup shows. <see cref="PlannedDay"/> is null on a rest day or with no split; then
/// <see cref="PickableDays"/> lists the split's days that have exercises. <see cref="Day"/> is the day the
/// <see cref="Exercises"/> and <see cref="Status"/> are for (null when there's nothing to check in).
/// </summary>
public record CheckInInfo(DateOnly Date, bool HasSplit, bool Logged, CheckInDay? PlannedDay,
    IReadOnlyList<CheckInDay> PickableDays, CheckInDay? Day, IReadOnlyList<CheckInExerciseInfo> Exercises, CheckInStatus Status);

/// <summary>One exercise of the dashboard's "Today's workout" card. <see cref="LastTopSet"/> is the heaviest working
/// set of the last session before today (most reps on a tie), or null with no earlier session.</summary>
public record TodayExercise(string Name, int WarmupSets, int WorkingSets, SetEntry? LastTopSet);

/// <summary>The dashboard's "Today's workout": the planned day (the same plan as the check-in, so it follows shifts
/// and make-up chains) and its exercises in order. <see cref="DayName"/> is null on a rest day or with no split.</summary>
public record TodayWorkoutInfo(DateOnly Date, bool HasSplit, string? DayName, IReadOnlyList<TodayExercise> Exercises);

public class WorkoutService(IDbContextFactory<AppDbContext> dbFactory, StreakService streaks, PointsService points)
{
    private record Plan(int SplitId, TodayWorkout Workout, SplitDay? Day);

    public const string NeedsChoiceError = "Choose what to do about yesterday's missed workout first.";

    public async Task<TodayInfo> GetTodayAsync(int userId, DateOnly? date = null)
    {
        var today = date ?? Today();
        await using var db = await dbFactory.CreateDbContextAsync();

        var logged = await db.WorkoutLogs.AnyAsync(w => w.UserId == userId && w.Date == today);
        var plan = await PlanTodayAsync(db, userId, today);

        return plan is null
            ? new TodayInfo(today, false, true, "", logged)
            : new TodayInfo(today, true, plan.Workout.IsTraining, plan.Workout.Title, logged);
    }

    public async Task<TodayWorkoutInfo> GetTodayWorkoutAsync(int userId, DateOnly? today = null)
    {
        var date = today ?? Today();
        await using var db = await dbFactory.CreateDbContextAsync();
        var plan = await PlanTodayAsync(db, userId, date);
        if (plan?.Day is not { } day) return new TodayWorkoutInfo(date, plan is not null, null, []);

        var exercises = await ExercisesAsync(db, userId, day.Id, date);
        return new TodayWorkoutInfo(date, true, day.Name, exercises.Select(e => new TodayExercise(e.Name, e.WarmupSets, e.WorkingSets,
            e.LastSession.Where(s => s.Kind == SetKind.Working)
                .OrderByDescending(s => s.WeightKg).ThenByDescending(s => s.Reps).FirstOrDefault())).ToList());
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
        var plan = await PlanTodayAsync(db, userId, date);
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
    /// second call just returns the current streak. Refused while yesterday's missed workout still needs a choice
    /// (<see cref="StreakStatus.NeedsChoice"/>).
    /// </summary>
    public async Task<LogResult> LogTodayAsync(int userId, int? splitDayId = null, DateOnly? today = null)
    {
        var date = today ?? Today();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            if (!await db.WorkoutLogs.AnyAsync(w => w.UserId == userId && w.Date == date))
            {
                if ((await streaks.GetAsync(userId, date)).Status == StreakStatus.NeedsChoice)
                    return new LogResult(false, NeedsChoiceError);

                var plan = await PlanTodayAsync(db, userId, date);
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

                db.WorkoutLogs.Add(new WorkoutLog
                {
                    UserId = userId, Date = date, SplitDayId = day.SplitDayId,
                    GymId = await AutomaticGymAsync(db, userId)
                });
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

    /// <summary>The gym a new check-in gets by itself: the user's gym when "Ask for gym" is off, as long as it's still
    /// pickable. Otherwise none; with "Ask for gym" on, Home asks with that gym preselected.</summary>
    private static async Task<int?> AutomaticGymAsync(AppDbContext db, int userId)
    {
        var p = await db.UserProfiles.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.AskForGymAfterCheckIn, x.HomeGymId }).FirstOrDefaultAsync();
        if (p is not { AskForGymAfterCheckIn: false, HomeGymId: { } gymId }) return null;
        return await GymService.Pickable(db, userId).AnyAsync(g => g.Id == gymId) ? gymId : null;
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

    private static DateOnly Today() => AppClock.Today;

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
            .Where(s => s.UserId == userId && s.Date <= date && ids.Contains(s.ExerciseId))
            .ToListAsync();
        var byExercise = sets.ToLookup(s => s.ExerciseId, s => new SetEntry(s.Date, s.SetNumber, s.Kind, s.Reps, s.WeightKg));

        return rows.Select(r =>
        {
            var all = byExercise[r.ExerciseId].ToList();
            return new CheckInExerciseInfo(r.ExerciseId, r.Name, r.WarmupSets, r.WorkingSets,
                all.Where(s => s.Date == date).OrderBy(s => s.SetNumber).ToList(),
                PrCalculator.LastSession(all, date));
        }).ToList();
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

    /// <summary>Today's plan. While a "make it up before my next rest day" chain is open, today's workout is
    /// yesterday's scheduled one (see <see cref="StreakService.MakeUpDayAsync"/>), so that day is planned instead.</summary>
    private async Task<Plan?> PlanTodayAsync(AppDbContext db, int userId, DateOnly today) =>
        await PlanAsync(db, userId, await streaks.MakeUpDayAsync(userId, today) ?? today);

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
