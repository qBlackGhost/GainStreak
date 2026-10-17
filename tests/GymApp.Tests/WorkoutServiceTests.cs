using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class WorkoutServiceTests : IDisposable
{
    // 2026-10-05 is a Monday. Alice trains Mon (Push), Tue (Pull), Thu (Legs), Fri (Arms); Wed, Sat, Sun are rest days
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int n) => Mon.AddDays(n);
    private static readonly DateOnly Fri = D(4);
    private static readonly DateOnly Wed = D(2);

    private readonly TestDb _db = new();
    private readonly WorkoutService _svc;
    private readonly SetService _sets;
    private readonly int _alice;
    private readonly int _bob;
    private readonly int _push, _pull, _legs, _arms;

    public WorkoutServiceTests()
    {
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
        var days = _db.AddSplit(_alice,
            ("Push", DayOfWeek.Monday), ("Pull", DayOfWeek.Tuesday), ("Legs", DayOfWeek.Thursday), ("Arms", DayOfWeek.Friday));
        (_push, _pull, _legs, _arms) = (days[0], days[1], days[2], days[3]);
        using (var db = _db.CreateDbContext())
        {
            db.UserSchedules.Add(new UserSchedule { UserId = _alice, EffectiveFrom = Mon, WeekPattern = "WWRWWRR" });
            db.SaveChanges();
        }
        _sets = new SetService(_db);
        _svc = new WorkoutService(_db, new StreakService(_db), new PointsService(_db));
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Saves <paramref name="working"/> working sets (after <paramref name="warmups"/> warm-ups) for the exercise.</summary>
    private async Task Fill(int exerciseId, DateOnly date, int working = 3, int warmups = 0)
    {
        var rows = Enumerable.Repeat(new SetInput(SetKind.Warmup, 10, 20), warmups)
            .Concat(Enumerable.Repeat(new SetInput(SetKind.Working, 8, 60), working))
            .ToList();
        Assert.True((await _sets.SaveDayAsync(_alice, exerciseId, rows, date)).Success);
    }

    private void Log(int userId, DateOnly date, int? splitDayId = null)
    {
        using var db = _db.CreateDbContext();
        db.WorkoutLogs.Add(new WorkoutLog { UserId = userId, Date = date, SplitDayId = splitDayId });
        db.SaveChanges();
    }

    private List<WorkoutLog> Logs(int userId)
    {
        using var db = _db.CreateDbContext();
        return db.WorkoutLogs.AsNoTracking().Where(w => w.UserId == userId).OrderBy(w => w.Date).ToList();
    }

    private List<PointsEntry> Entries(int userId)
    {
        using var db = _db.CreateDbContext();
        return db.PointsEntries.AsNoTracking().Where(p => p.UserId == userId).ToList();
    }

    private int CachedPoints(int userId)
    {
        using var db = _db.CreateDbContext();
        return db.Users.Single(u => u.Id == userId).Points;
    }

    private (int Current, int Longest) Streaks(int userId)
    {
        using var db = _db.CreateDbContext();
        var u = db.Users.Single(x => x.Id == userId);
        return (u.CurrentStreak, u.LongestStreak);
    }

    private int SetCount(int userId, DateOnly date)
    {
        using var db = _db.CreateDbContext();
        return db.SetLogs.Count(s => s.UserId == userId && s.Date == date);
    }

    // ===== Check-in info =====

    [Fact] public async Task GetCheckIn_TrainingDay_ListsThePlannedDaysExercisesInOrder_WithTodaysSets()
    {
        var ex = _db.AddDayExercises(_alice, _arms, 3, warmupSets: 1);
        await Fill(ex[1], Fri);

        var info = await _svc.GetCheckInAsync(_alice, today: Fri);

        Assert.True(info.HasSplit);
        Assert.False(info.Logged);
        Assert.Equal(new CheckInDay(_arms, "Arms"), info.PlannedDay);
        Assert.Equal(info.PlannedDay, info.Day);
        Assert.Empty(info.PickableDays);
        Assert.Equal(ex, info.Exercises.Select(e => e.ExerciseId));
        Assert.All(info.Exercises, e => Assert.Equal((1, 3), (e.WarmupSets, e.WorkingSets)));
        Assert.Equal([0, 3, 0], info.Exercises.Select(e => e.TodaySets.Count));
        Assert.Equal(new CheckInStatus(1, 2, 3, false), info.Status);
    }

    [Fact] public async Task GetCheckIn_RestDay_OffersOnlyDaysWithExercises_AndFollowsThePick()
    {
        _db.AddDayExercises(_alice, _push, 2);
        _db.AddDayExercises(_alice, _legs, 1);

        var info = await _svc.GetCheckInAsync(_alice, today: Wed);
        Assert.Null(info.PlannedDay);
        Assert.Equal([_push, _legs], info.PickableDays.Select(d => d.SplitDayId));
        Assert.Equal(_push, info.Day!.SplitDayId);   // first pickable by default
        Assert.Equal(2, info.Exercises.Count);

        var legs = await _svc.GetCheckInAsync(_alice, _legs, Wed);
        Assert.Equal(_legs, legs.Day!.SplitDayId);
        Assert.Single(legs.Exercises);
    }

    [Fact] public async Task GetCheckIn_NoSplit_HasNothingToCheckIn()
    {
        var info = await _svc.GetCheckInAsync(_bob, today: Fri);
        Assert.False(info.HasSplit);
        Assert.Null(info.Day);
        Assert.Empty(info.PickableDays);
        Assert.False(info.Status.CanLog);
    }

    // ===== The 50% rule =====

    [Fact] public async Task Log_FailsWithNoSets()
    {
        _db.AddDayExercises(_alice, _arms, 3);
        var r = await _svc.LogTodayAsync(_alice, today: Fri);
        Assert.False(r.Success);
        Assert.Equal("Fill in at least 2 of 3 exercises to log this workout.", r.Error);
        Assert.Empty(Logs(_alice));
    }

    [Fact] public async Task Log_FailsWithFewerThanHalfFilled()
    {
        var ex = _db.AddDayExercises(_alice, _arms, 3);
        await Fill(ex[0], Fri);
        await Fill(ex[1], Fri, working: 2);   // one short

        Assert.False((await _svc.LogTodayAsync(_alice, today: Fri)).Success);
        Assert.Empty(Logs(_alice));
    }

    [Fact] public async Task Log_SucceedsAtExactlyHalf_AndStoresThePlannedDay()
    {
        var ex = _db.AddDayExercises(_alice, _arms, 4);
        await Fill(ex[0], Fri);
        await Fill(ex[3], Fri);

        var r = await _svc.LogTodayAsync(_alice, _push, Fri);   // the pick is ignored on a training day
        Assert.True(r.Success);
        var log = Assert.Single(Logs(_alice));
        Assert.Equal((Fri, (int?)_arms), (log.Date, log.SplitDayId));
        Assert.Contains(Entries(_alice), p => p.Reason == PointsReason.Workout && p.Date == Fri);
    }

    [Fact] public async Task Log_FailsWithBuildAWorkout_WhenTheDayHasNoExercises()
    {
        var r = await _svc.LogTodayAsync(_alice, today: Fri);
        Assert.False(r.Success);
        Assert.Equal("Build a workout for Arms first.", r.Error);
    }

    [Fact] public async Task Log_FailsWithNoSplit()
    {
        var r = await _svc.LogTodayAsync(_bob, today: Fri);
        Assert.False(r.Success);
        Assert.Empty(Logs(_bob));
    }

    [Fact] public async Task Log_WarmupOnlySetsDontCount()
    {
        var ex = _db.AddDayExercises(_alice, _arms, 1, warmupSets: 2);
        await Fill(ex[0], Fri, working: 0, warmups: 5);
        Assert.False((await _svc.LogTodayAsync(_alice, today: Fri)).Success);
    }

    [Fact] public async Task Log_AlreadyLoggedToday_ReturnsTheStreakWithoutRecheckingTheRule()
    {
        Log(_alice, Fri, _arms);
        var r = await _svc.LogTodayAsync(_alice, today: Fri);
        Assert.True(r.Success);
        Assert.Single(Logs(_alice));
    }

    // ===== Rest days =====

    [Fact] public async Task RestDay_LogWithAFilledPickedDay_Succeeds()
    {
        var ex = _db.AddDayExercises(_alice, _legs, 2);
        await Fill(ex[0], Wed);

        var r = await _svc.LogTodayAsync(_alice, _legs, Wed);
        Assert.True(r.Success);
        Assert.Equal(_legs, Assert.Single(Logs(_alice)).SplitDayId);
    }

    [Fact] public async Task RestDay_WithoutAPick_Fails()
    {
        var ex = _db.AddDayExercises(_alice, _legs, 1);
        await Fill(ex[0], Wed);
        var r = await _svc.LogTodayAsync(_alice, today: Wed);
        Assert.False(r.Success);
        Assert.Equal("Pick the day you trained.", r.Error);
    }

    [Fact] public async Task RestDay_ADayThatIsntTheUsersSplit_Fails()
    {
        var bobsDay = _db.AddSplit(_bob, ("Chest", DayOfWeek.Wednesday))[0];
        var ex = _db.AddDayExercises(_alice, _legs, 1);
        await Fill(ex[0], Wed);

        Assert.False((await _svc.LogTodayAsync(_alice, bobsDay, Wed)).Success);
        Assert.False((await _svc.LogTodayAsync(_alice, 99999, Wed)).Success);
        Assert.Empty(Logs(_alice));
    }

    [Fact] public async Task RestDay_PickedDayNotFilled_Fails()
    {
        _db.AddDayExercises(_alice, _legs, 2);
        var r = await _svc.LogTodayAsync(_alice, _legs, Wed);
        Assert.False(r.Success);
        Assert.Equal("Fill in at least 1 of 2 exercises to log this workout.", r.Error);
    }

    // ===== Undo =====

    [Fact] public async Task Undo_RemovesTheLog_TheWorkoutPoints_AndTheWeekBonus_AndTheStreakGoesBack()
    {
        Log(_alice, D(0), _push);
        Log(_alice, D(1), _pull);
        Log(_alice, D(3), _legs);
        var ex = _db.AddDayExercises(_alice, _arms, 1);
        await Fill(ex[0], Fri);

        var logged = await _svc.LogTodayAsync(_alice, today: Fri);
        Assert.Equal(5, logged.Streak!.Current);   // rest days count: Mon to Fri
        Assert.Contains(Entries(_alice), p => p.Reason == PointsReason.WeekComplete);
        Assert.Equal(4 * 5 + 50, CachedPoints(_alice));

        var undone = await _svc.UndoTodayAsync(_alice, Fri);

        Assert.True(undone.Success);
        Assert.Equal(4, undone.Streak!.Current);   // Mon to Thu, today pending again
        Assert.Equal(StreakStatus.PendingToday, undone.Streak.Status);
        Assert.Equal(4, undone.Streak.Longest);   // today's streak was the best, so the best goes back too
        Assert.Equal((4, 4), Streaks(_alice));
        Assert.Equal([D(0), D(1), D(3)], Logs(_alice).Select(w => w.Date));
        Assert.DoesNotContain(Entries(_alice), p => p.Date == Fri);
        Assert.DoesNotContain(Entries(_alice), p => p.Reason == PointsReason.WeekComplete);
        Assert.Equal(3 * 5, CachedPoints(_alice));
        Assert.Equal(3 * 5, undone.Points!.NewTotal);
    }

    [Fact] public async Task Undo_KeepsAnOlderBestStreak()
    {
        using (var db = _db.CreateDbContext())
        {
            db.Users.Single(u => u.Id == _alice).LongestStreak = 30;
            db.SaveChanges();
        }
        Log(_alice, D(3), _legs);
        var ex = _db.AddDayExercises(_alice, _arms, 1);
        await Fill(ex[0], Fri);
        await _svc.LogTodayAsync(_alice, today: Fri);

        var undone = await _svc.UndoTodayAsync(_alice, Fri);

        Assert.Equal(30, undone.Streak!.Longest);
        Assert.Equal(30, Streaks(_alice).Longest);
    }

    [Fact] public async Task Undo_WithNothingLogged_LeavesTheBestStreak()
    {
        using (var db = _db.CreateDbContext())
        {
            var u = db.Users.Single(x => x.Id == _alice);
            (u.CurrentStreak, u.LongestStreak) = (12, 12);   // a best the logs alone no longer give
            db.SaveChanges();
        }
        await _svc.UndoTodayAsync(_alice, Fri);
        Assert.Equal(12, Streaks(_alice).Longest);
    }

    [Fact] public async Task Undo_KeepsTodaysSets()
    {
        var ex = _db.AddDayExercises(_alice, _arms, 1, warmupSets: 1);
        await Fill(ex[0], Fri, warmups: 1);
        await _svc.LogTodayAsync(_alice, today: Fri);

        await _svc.UndoTodayAsync(_alice, Fri);

        Assert.Equal(4, SetCount(_alice, Fri));
        var info = await _svc.GetCheckInAsync(_alice, today: Fri);
        Assert.False(info.Logged);
        Assert.True(info.Status.CanLog);   // re-checking in is pre-filled
    }

    [Fact] public async Task Undo_RemovesTodaysGymChoice()
    {
        var ex = _db.AddDayExercises(_alice, _arms, 1);
        await Fill(ex[0], Fri);
        await _svc.LogTodayAsync(_alice, today: Fri);
        int gymId;
        using (var db = _db.CreateDbContext())
        {
            var city = new City { OsmType = OsmElementType.Relation, OsmId = 1, Name = "T", DisplayName = "T" };
            var gym = new Gym { City = city, Name = "A", OwnerUserId = _alice, IsActive = true };
            db.Gyms.Add(gym);
            db.SaveChanges();
            gymId = gym.Id;
        }
        var gyms = new GymService(_db);
        Assert.True((await gyms.SetGymForTodayAsync(_alice, gymId, Fri)).Success);

        await _svc.UndoTodayAsync(_alice, Fri);

        var today = await gyms.GetTodayAsync(_alice, Fri);
        Assert.False(today.LoggedToday);
        Assert.Null(today.Gym);
    }

    [Fact] public async Task Undo_WithNothingLogged_IsHarmless()
    {
        var r = await _svc.UndoTodayAsync(_alice, Fri);
        Assert.True(r.Success);
        Assert.Equal(0, r.Streak!.Current);
        Assert.Empty(Logs(_alice));
    }

    [Fact] public async Task Undo_NeverTouchesYesterday_OrOtherUsers()
    {
        Log(_alice, D(3), _legs);
        Log(_bob, Fri);

        await _svc.UndoTodayAsync(_alice, Fri);

        Assert.Equal([D(3)], Logs(_alice).Select(w => w.Date));
        Assert.Single(Logs(_bob));
    }

    [Fact] public async Task LogUndoLogAgain_EndsWithOneLogAndOneWorkoutEntry()
    {
        var ex = _db.AddDayExercises(_alice, _arms, 1);
        await Fill(ex[0], Fri);

        Assert.True((await _svc.LogTodayAsync(_alice, today: Fri)).Success);
        Assert.True((await _svc.UndoTodayAsync(_alice, Fri)).Success);
        Assert.True((await _svc.LogTodayAsync(_alice, today: Fri)).Success);

        Assert.Single(Logs(_alice));
        Assert.Single(Entries(_alice), p => p.Reason == PointsReason.Workout && p.Date == Fri);
        Assert.Equal(5, CachedPoints(_alice));
    }
}
