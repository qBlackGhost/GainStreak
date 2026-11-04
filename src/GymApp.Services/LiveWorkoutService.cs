using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>
/// An open live workout, as the card shows it: the day being done, its exercises in plan order with today's sets,
/// the exercise the user picked and the ones skipped, the <see cref="CheckInRule"/> status and whether today's log
/// already exists (it's written silently at half, so the card must never show this).
/// </summary>
public record LiveWorkoutInfo(int Id, DateOnly Date, int SplitDayId, string DayName, DateTime StartedAt, DateTime LastActiveAt,
    int? CurrentExerciseId, IReadOnlyList<int> SkippedExerciseIds, IReadOnlyList<CheckInExerciseInfo> Exercises,
    CheckInStatus Status, bool Logged, int? VerifiedGymId = null, string? VerifiedGymName = null)
{
    /// <summary>The phone was found at one of the user's gyms during this workout (18c).</summary>
    public bool IsVerified => VerifiedGymId is not null;

    /// <summary>The exercises as <see cref="LiveWorkoutRules"/> sees them, in plan order.</summary>
    public IReadOnlyList<LiveExercise> Plan => Exercises
        .Select(e => new LiveExercise(e.ExerciseId, e.WarmupSets, e.WorkingSets, e.TodaySets)).ToList();
}

/// <summary>Start: the open session, or why there's none. <see cref="NeedsDay"/>: a rest day, so the user picks one of
/// <see cref="PickableDays"/> first (like the check-in's day picker).</summary>
public record LiveStartResult(bool Success, string? Error = null, LiveWorkoutInfo? Workout = null, bool NeedsDay = false,
    IReadOnlyList<CheckInDay>? PickableDays = null);

/// <summary>A session that stopped by itself (4 hours, or midnight), for Home's one-time note: how much was done and,
/// when it counted, the points the workout earned.</summary>
public record LiveStopNote(int Id, bool AtMidnight, int Filled, int Total, bool Counted, int WorkoutPoints);

public record LiveSaveResult(bool Success, string? Error = null, LiveWorkoutInfo? Workout = null);

/// <summary>How a session ended. <see cref="WorkoutPoints"/> and <see cref="CardioPoints"/> are what today's log earns
/// (0 when it didn't count); <see cref="Log"/> carries the streak and points sync for Home.</summary>
public record LiveEndResult(bool Success, string? Error, LiveOutcome Outcome, int Filled, int Total, int WorkoutPoints,
    int CardioPoints, LogResult? Log);

/// <summary>
/// Phase 18's live workout on phones: start, one set at a time through <see cref="SetService"/> (so limits and PRs behave
/// as everywhere else), skip, pick another exercise, finish or end. As soon as half the exercises are filled it logs
/// today's workout through <see cref="WorkoutService.LogTodayAsync"/>, without telling the card; from then on every saved
/// set updates the log's completion (<see cref="SetService"/> does that). Today only, like all logging.
/// </summary>
/// <param name="clock">For tests; the app uses <see cref="AppClock"/>, so the TestBench can move it.</param>
public class LiveWorkoutService(IDbContextFactory<AppDbContext> dbFactory, WorkoutService workouts, SetService sets,
    StreakService streaks, TimeProvider? clock = null)
{
    private DateTime Now => clock is null ? AppClock.Now : clock.GetLocalNow().DateTime;
    private DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>The open session, or null. A session past <see cref="LiveWorkoutRules.MustStop"/> (4 hours, or
    /// midnight) is ended here first.</summary>
    public async Task<LiveWorkoutInfo?> GetOpenAsync(int userId)
    {
        LiveWorkout? open;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            open = await db.LiveWorkouts.FirstOrDefaultAsync(l => l.UserId == userId && l.EndedAt == null);
            if (open is null) return null;
            if (LiveWorkoutRules.MustStop(open.StartedAt, open.Date, Now))
            {
                open.EndedAt = LiveWorkoutRules.StopAt(open.StartedAt, open.Date);
                open.AutoStop = LiveWorkoutRules.StopsAtMidnight(open.StartedAt, open.Date) ? LiveWorkout.Midnight : LiveWorkout.FourHours;
                await db.SaveChangesAsync();
                return null;
            }
        }
        return await InfoAsync(open);
    }

    /// <summary>Whether a session is open and still running, without loading it.</summary>
    public async Task<bool> IsOpenAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var open = await db.LiveWorkouts.AsNoTracking().Where(l => l.UserId == userId && l.EndedAt == null)
            .Select(l => new { l.StartedAt, l.Date }).FirstOrDefaultAsync();
        return open is not null && !LiveWorkoutRules.MustStop(open.StartedAt, open.Date, Now);
    }

    /// <summary>Ends an open session that's past 4 hours or midnight. True when it did (Home then shows the note).
    /// Checked on coming back to the front and once a minute while the app is open.</summary>
    public async Task<bool> StopIfDueAsync(int userId)
    {
        await using (var db = await dbFactory.CreateDbContextAsync())
            if (!await db.LiveWorkouts.AnyAsync(l => l.UserId == userId && l.EndedAt == null)) return false;
        return await GetOpenAsync(userId) is null;
    }

    /// <summary>The user is here: the 12 minutes away start again. Also when the app goes to the background, so
    /// "away" counts from then.</summary>
    public Task TouchAsync(int userId) => UpdateAsync(userId, _ => { });

    /// <summary>How long since the open session was last touched; null without one.</summary>
    public async Task<TimeSpan?> AwayForAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var last = await db.LiveWorkouts.Where(l => l.UserId == userId && l.EndedAt == null)
            .Select(l => (DateTime?)l.LastActiveAt).FirstOrDefaultAsync();
        return last is { } t ? Now - t : null;
    }

    /// <summary>The newest session that stopped by itself and whose note Home hasn't shown yet.</summary>
    public async Task<LiveStopNote?> GetStopNoteAsync(int userId)
    {
        LiveWorkout? l;
        WorkoutLog? log;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            l = await db.LiveWorkouts.AsNoTracking()
                .Where(x => x.UserId == userId && x.AutoStop != null && !x.StopNoteSeen)
                .OrderByDescending(x => x.EndedAt).FirstOrDefaultAsync();
            if (l is null) return null;
            log = await db.WorkoutLogs.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == userId && w.Date == l.Date);
        }
        var exercises = await workouts.GetDayExercisesAsync(userId, l.SplitDayId, l.Date);
        var status = CheckInRule.Evaluate(exercises.Select(e => new CheckInExercise(e.ExerciseId, e.WorkingSets, e.TodaySets)));
        var (filled, total) = (log?.FilledExercises ?? status.Filled, log?.TotalExercises ?? status.Total);
        var points = log is null ? 0 : PointsCalculator.WorkoutPointsFor(new WorkoutRecord(l.Date, filled, total, log.Verified));
        return new LiveStopNote(l.Id, l.AutoStop == LiveWorkout.Midnight, filled, total, log is not null, points);
    }

    /// <summary>The note was shown; it never comes back.</summary>
    public async Task MarkStopNoteSeenAsync(int userId, int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.LiveWorkouts.Where(l => l.UserId == userId && l.Id == id).ExecuteUpdateAsync(u => u.SetProperty(l => l.StopNoteSeen, true));
    }

    /// <summary>Notification permission is asked once, at the first Start workout. True the first time this is called.</summary>
    public async Task<bool> FirstStartAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.Where(u => u.Id == userId && !u.WorkoutNotificationsAsked)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.WorkoutNotificationsAsked, true)) > 0;
    }

    /// <summary>
    /// Starts today's live workout: the planned day, or on a rest day <paramref name="splitDayId"/>, one of the split's
    /// days (without it, <see cref="LiveStartResult.NeedsDay"/>). An open session is returned as it is. Refused when
    /// today is already logged, while yesterday's missed workout needs a choice, with no split, or when the day has
    /// no exercises.
    /// </summary>
    public async Task<LiveStartResult> StartAsync(int userId, int? splitDayId = null)
    {
        if (await GetOpenAsync(userId) is { } running) return new LiveStartResult(true, Workout: running);

        var today = Today;
        var checkIn = await workouts.GetCheckInAsync(userId, splitDayId, today);
        if (checkIn.Logged) return new LiveStartResult(false, "Today's workout is already logged.");
        if ((await streaks.GetAsync(userId, today)).Status == StreakStatus.NeedsChoice)
            return new LiveStartResult(false, WorkoutService.NeedsChoiceError);
        if (!checkIn.HasSplit) return new LiveStartResult(false, "Pick a split and build a workout first.");
        if (checkIn.PlannedDay is null && splitDayId is null)
            return checkIn.PickableDays.Count == 0
                ? new LiveStartResult(false, "You haven't built any workouts yet.")
                : new LiveStartResult(false, NeedsDay: true, PickableDays: checkIn.PickableDays);
        if (checkIn.Day is not { } day) return new LiveStartResult(false, "That day isn't in your split.");
        if (checkIn.Exercises.Count == 0) return new LiveStartResult(false, $"You haven't built a workout for {day.Name} yet.");

        var now = Now;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.LiveWorkouts.Add(new LiveWorkout
                { UserId = userId, Date = today, SplitDayId = day.SplitDayId, StartedAt = now, LastActiveAt = now });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { /* another tap started one: the unique open-session index */ }
        }
        return await GetOpenAsync(userId) is { } started
            ? new LiveStartResult(true, Workout: started)
            : new LiveStartResult(false, "Could not start the workout.");
    }

    /// <summary>
    /// Saves one set of the open session's <paramref name="exerciseId"/>: a new one (a warm-up after the warm-ups, a
    /// working set at the end), or with <paramref name="replaceIndex"/> the set at that place among today's sets
    /// (warm-ups first, then working sets, as the card's chips show them). A new set that fills a picked exercise
    /// hands the card back to the plan's order. Logs today's workout once half the exercises are filled.
    /// </summary>
    public async Task<LiveSaveResult> SaveSetAsync(int userId, int exerciseId, SetInput set, int? replaceIndex = null)
    {
        if (await GetOpenAsync(userId) is not { } live) return new LiveSaveResult(false, "This workout has ended.");
        if (live.Exercises.FirstOrDefault(e => e.ExerciseId == exerciseId) is not { } exercise)
            return new LiveSaveResult(false, "That exercise isn't in this workout.");

        var ordered = Ordered(exercise.TodaySets).Select(s => new SetInput(s.Kind, s.Reps, s.WeightKg)).ToList();
        if (replaceIndex is { } i)
        {
            if (i < 0 || i >= ordered.Count) return new LiveSaveResult(false, "That set isn't logged.");
            ordered[i] = set with { Kind = ordered[i].Kind };
        }
        else if (set.Kind == SetKind.Warmup)
            ordered.Insert(ordered.Count(s => s.Kind == SetKind.Warmup), set);
        else
            ordered.Add(set);

        var saved = await sets.SaveDayAsync(userId, exerciseId, ordered, live.Date);
        if (!saved.Success) return new LiveSaveResult(false, saved.Error);

        var after = await GetOpenAsync(userId);
        if (after is null) return new LiveSaveResult(false, "This workout has ended.");
        var filledNow = after.Plan.First(e => e.ExerciseId == exerciseId).IsFilled;
        await UpdateAsync(userId, l => { if (replaceIndex is null && filledNow) l.CurrentExerciseId = null; });

        if (!after.Logged && after.Status.CanLog)
        {
            var log = await workouts.LogTodayAsync(userId, after.SplitDayId, live.Date, verifiedGymId: after.VerifiedGymId);
            if (!log.Success) return new LiveSaveResult(false, log.Error);
        }
        return new LiveSaveResult(true, Workout: await GetOpenAsync(userId));
    }

    /// <summary>Puts <paramref name="exerciseId"/> on the card (any exercise of the day, in any order); null goes back
    /// to the plan's order.</summary>
    public async Task<LiveWorkoutInfo?> PickAsync(int userId, int? exerciseId)
    {
        await UpdateAsync(userId, l => l.CurrentExerciseId = exerciseId);
        return await GetOpenAsync(userId);
    }

    /// <summary>"Skip for now": the exercise moves behind the others until they're done; tapping it in the list still
    /// brings it back.</summary>
    public async Task<LiveWorkoutInfo?> SkipAsync(int userId, int exerciseId)
    {
        await UpdateAsync(userId, l =>
        {
            l.SkippedExerciseIds = LiveWorkoutRules.FormatIds(
                LiveWorkoutRules.ParseIds(l.SkippedExerciseIds).Where(id => id != exerciseId).Append(exerciseId));
            l.CurrentExerciseId = null;
        });
        return await GetOpenAsync(userId);
    }

    /// <summary>
    /// Finish: half or more of the exercises filled, so today is logged (it already is, silently); saves the cardio
    /// onto the log and ends the session. Refused below half; that's <see cref="EndAsync"/>.
    /// </summary>
    public async Task<LiveEndResult> FinishAsync(int userId, int? cardioMinutes = null)
    {
        if (await GetOpenAsync(userId) is not { } live)
            return new LiveEndResult(false, "This workout has ended.", LiveOutcome.NotCounted, 0, 0, 0, 0, null);
        var (filled, total) = (live.Status.Filled, live.Status.Total);
        if (!live.Status.CanLog)
            return new LiveEndResult(false, $"Fill in at least {live.Status.Required} of {total} exercises to finish.",
                LiveOutcome.NotCounted, filled, total, 0, 0, null);

        var log = await workouts.LogTodayAsync(userId, live.SplitDayId, live.Date, cardioMinutes, live.VerifiedGymId);
        if (!log.Success) return new LiveEndResult(false, log.Error, LiveOutcome.NotCounted, filled, total, 0, 0, null);
        var cardio = await workouts.SetCardioForTodayAsync(userId, cardioMinutes, live.Date);
        if (!cardio.Success) return new LiveEndResult(false, cardio.Error, LiveOutcome.NotCounted, filled, total, 0, 0, null);

        await UpdateAsync(userId, l => l.EndedAt = Now);
        var points = PointsCalculator.WorkoutPointsFor(new WorkoutRecord(live.Date, filled, total, live.IsVerified));
        return new LiveEndResult(true, null, LiveWorkoutRules.Outcome(filled, total), filled, total, points,
            PointsCalculator.CardioPoints(cardioMinutes ?? 0), log with { Points = cardio.Points ?? log.Points });
    }

    /// <summary>"End workout": ends the session without logging. The sets stay. With half or more filled the workout
    /// is already logged and stays logged.</summary>
    public async Task<LiveEndResult> EndAsync(int userId)
    {
        if (await GetOpenAsync(userId) is not { } live)
            return new LiveEndResult(false, "This workout has ended.", LiveOutcome.NotCounted, 0, 0, 0, 0, null);
        await UpdateAsync(userId, l => l.EndedAt = Now);
        var outcome = live.Logged ? LiveWorkoutRules.Outcome(live.Status.Filled, live.Status.Total) : LiveOutcome.NotCounted;
        return new LiveEndResult(true, null, outcome, live.Status.Filled, live.Status.Total, 0, 0, null);
    }

    /// <summary>Warm-ups first, then working sets, each in the order logged: the card's chips.</summary>
    public static IReadOnlyList<SetEntry> Ordered(IEnumerable<SetEntry> sets)
    {
        var list = sets.OrderBy(s => s.SetNumber).ToList();
        return list.Where(s => s.Kind == SetKind.Warmup).Concat(list.Where(s => s.Kind == SetKind.Working)).ToList();
    }

    private async Task UpdateAsync(int userId, Action<LiveWorkout> change)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var open = await db.LiveWorkouts.FirstOrDefaultAsync(l => l.UserId == userId && l.EndedAt == null);
        if (open is null) return;
        change(open);
        open.LastActiveAt = Now;
        await db.SaveChangesAsync();
    }

    private async Task<LiveWorkoutInfo> InfoAsync(LiveWorkout l)
    {
        string dayName;
        bool logged;
        string? gymName = null;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            dayName = await db.SplitDays.Where(d => d.Id == l.SplitDayId).Select(d => d.Name).FirstOrDefaultAsync() ?? "Workout";
            logged = await db.WorkoutLogs.AnyAsync(w => w.UserId == l.UserId && w.Date == l.Date);
            if (l.VerifiedGymId is { } gymId) gymName = await db.Gyms.Where(g => g.Id == gymId).Select(g => g.Name).FirstOrDefaultAsync();
        }
        var exercises = await workouts.GetDayExercisesAsync(l.UserId, l.SplitDayId, l.Date);
        var status = CheckInRule.Evaluate(exercises.Select(e => new CheckInExercise(e.ExerciseId, e.WorkingSets, e.TodaySets)));
        return new LiveWorkoutInfo(l.Id, l.Date, l.SplitDayId, dayName, l.StartedAt, l.LastActiveAt, l.CurrentExerciseId,
            LiveWorkoutRules.ParseIds(l.SkippedExerciseIds), exercises, status, logged, gymName is null ? null : l.VerifiedGymId, gymName);
    }

    // ===== Verified at gym (18c) =====

    /// <summary>
    /// Checks one fix from the phone against the user's gyms that have a position (<see cref="GymProximity"/>): every
    /// active gym in the local database, so the cached OpenStreetMap gyms of their cities and their own custom gyms. No
    /// lookup around the position, which would send it to OpenStreetMap. Found: the session is verified at that gym,
    /// and today's log too when it's already written. The fix itself is never stored. Returns the session as it is now.
    /// </summary>
    public async Task<LiveWorkoutInfo?> VerifyAsync(int userId, LocationFix fix)
    {
        if (await GetOpenAsync(userId) is not { } live) return null;
        if (live.IsVerified) return live;

        List<GymSpot> gyms;
        await using (var db = await dbFactory.CreateDbContextAsync())
            gyms = await GymService.Pickable(db, userId).Where(g => g.Latitude != null && g.Longitude != null)
                .Select(g => new GymSpot(g.Id, g.Name, g.Latitude!.Value, g.Longitude!.Value, g.OwnerUserId != null))
                .ToListAsync();
        if (GymProximity.Nearest(fix, gyms) is not { } gym) return live;

        await UpdateAsync(userId, l => l.VerifiedGymId = gym.GymId);
        if (live.Logged) await workouts.MarkVerifiedAsync(userId, gym.GymId, live.Date);
        return await GetOpenAsync(userId);
    }
}
