using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

/// <summary>Phase 18's rules: the card's order, the 70% line, the points table and when a session stops.</summary>
public class LiveWorkoutRulesTests
{
    private static readonly DateOnly Day = new(2026, 10, 5);

    private static LiveExercise Ex(int id, int warmups, int working, int loggedWarmups = 0, int loggedWorking = 0) =>
        new(id, warmups, working, Enumerable.Range(1, loggedWarmups).Select(i => new SetEntry(Day, i, SetKind.Warmup, 10, 20))
            .Concat(Enumerable.Range(1, loggedWorking).Select(i => new SetEntry(Day, loggedWarmups + i, SetKind.Working, 8, 60)))
            .ToList());

    [Fact] public void NextSet_WarmupsFirst_ThenWorkingSets()
    {
        Assert.Equal(new LiveSet(SetKind.Warmup, 1), LiveWorkoutRules.NextSet(Ex(1, 2, 3)));
        Assert.Equal(new LiveSet(SetKind.Warmup, 2), LiveWorkoutRules.NextSet(Ex(1, 2, 3, loggedWarmups: 1)));
        Assert.Equal(new LiveSet(SetKind.Working, 1), LiveWorkoutRules.NextSet(Ex(1, 2, 3, loggedWarmups: 2)));
        Assert.Equal(new LiveSet(SetKind.Working, 3), LiveWorkoutRules.NextSet(Ex(1, 2, 3, 2, 2)));
        Assert.Null(LiveWorkoutRules.NextSet(Ex(1, 2, 3, 2, 3)));
    }

    [Fact] public void NextSet_FilledWithoutItsWarmups_IsDone() =>
        Assert.Null(LiveWorkoutRules.NextSet(Ex(1, 1, 3, loggedWorking: 3)));

    [Fact] public void NextSet_NoWorkingSetsPlanned_StillAsksForOne() =>
        Assert.Equal(new LiveSet(SetKind.Working, 1), LiveWorkoutRules.NextSet(Ex(1, 0, 0)));

    [Fact] public void Current_FollowsThePlan_SkippingFilledOnes()
    {
        var plan = new[] { Ex(1, 0, 3, loggedWorking: 3), Ex(2, 0, 3, loggedWorking: 1), Ex(3, 0, 3) };
        Assert.Equal(2, LiveWorkoutRules.Current(plan, null, [])!.ExerciseId);
    }

    [Fact] public void Current_SkippedOnesComeLast_InTheOrderSkipped()
    {
        var plan = new[] { Ex(1, 0, 3), Ex(2, 0, 3), Ex(3, 0, 3) };
        Assert.Equal(3, LiveWorkoutRules.Current(plan, null, [2, 1])!.ExerciseId);

        var rest = new[] { Ex(1, 0, 3), Ex(2, 0, 3), Ex(3, 0, 3, loggedWorking: 3) };
        Assert.Equal(2, LiveWorkoutRules.Current(rest, null, [2, 1])!.ExerciseId);
        Assert.Equal([3, 2, 1], LiveWorkoutRules.Order(rest, [2, 1]).Select(e => e.ExerciseId));
    }

    [Fact] public void Current_APickedExerciseWins_EvenAFilledOne()
    {
        var plan = new[] { Ex(1, 0, 3, loggedWorking: 3), Ex(2, 0, 3), Ex(3, 0, 3) };
        Assert.Equal(3, LiveWorkoutRules.Current(plan, 3, [])!.ExerciseId);
        Assert.Equal(1, LiveWorkoutRules.Current(plan, 1, [])!.ExerciseId);
        Assert.Equal(2, LiveWorkoutRules.Current(plan, 99, [])!.ExerciseId);   // gone from the day: the plan again
    }

    [Fact] public void Current_AllFilled_IsNull() =>
        Assert.Null(LiveWorkoutRules.Current([Ex(1, 0, 1, loggedWorking: 1), Ex(2, 1, 2, 1, 2)], null, []));

    [Fact] public void MustStop_AfterFourHours()
    {
        var start = new DateTime(2026, 10, 5, 17, 0, 0);
        Assert.False(LiveWorkoutRules.MustStop(start, Day, start.AddHours(3).AddMinutes(59)));
        Assert.True(LiveWorkoutRules.MustStop(start, Day, start.AddHours(4)));
    }

    [Fact] public void MustStop_AtMidnight()
    {
        var start = new DateTime(2026, 10, 5, 22, 30, 0);
        Assert.False(LiveWorkoutRules.MustStop(start, Day, new DateTime(2026, 10, 5, 23, 59, 59)));
        Assert.True(LiveWorkoutRules.MustStop(start, Day, new DateTime(2026, 10, 6, 0, 0, 0)));
    }

    [Theory]
    [InlineData(1, 4, LiveOutcome.NotCounted)]
    [InlineData(2, 4, LiveOutcome.Half)]
    [InlineData(3, 4, LiveOutcome.Full)]
    [InlineData(3, 6, LiveOutcome.Half)]
    [InlineData(4, 6, LiveOutcome.Half)]
    [InlineData(5, 6, LiveOutcome.Full)]
    [InlineData(0, 0, LiveOutcome.NotCounted)]
    public void Outcome(int filled, int total, LiveOutcome expected) =>
        Assert.Equal(expected, LiveWorkoutRules.Outcome(filled, total));

    [Theory]
    [InlineData(1, 1)] [InlineData(2, 2)] [InlineData(3, 3)] [InlineData(4, 3)] [InlineData(5, 4)]
    [InlineData(6, 5)] [InlineData(7, 5)] [InlineData(8, 6)] [InlineData(9, 7)] [InlineData(10, 7)]
    public void RequiredForFull_Is70PercentRoundedUp(int total, int required) =>
        Assert.Equal(required, CheckInRule.RequiredForFull(total));

    [Fact] public void Ids_RoundTrip() =>
        Assert.Equal([12, 7], LiveWorkoutRules.ParseIds(LiveWorkoutRules.FormatIds([12, 7, 12])));

    // ===== The points table =====

    [Theory]
    [InlineData(3, 4, true, 5)]    // verified, 70% or more
    [InlineData(4, 4, true, 5)]
    [InlineData(2, 4, true, 2)]    // verified, 50-69%
    [InlineData(3, 4, false, 2)]   // not verified, 70% or more
    [InlineData(2, 4, false, 2)]   // not verified, 50-69%
    [InlineData(4, 6, null, 2)]    // 67%: below 70% even before 18c
    [InlineData(5, 6, null, 5)]
    public void WorkoutPointsFor_EveryCellOfTheTable(int filled, int total, bool? verified, int points) =>
        Assert.Equal(points, PointsCalculator.WorkoutPointsFor(new WorkoutRecord(Day, filled, total, verified)));

    [Fact] public void WorkoutPointsFor_OldLogs_KeepTheFullPoints()
    {
        Assert.Equal(5, PointsCalculator.WorkoutPointsFor(new WorkoutRecord(Day, null, null, null)));
        Assert.Equal(5, PointsCalculator.WorkoutPointsFor(new WorkoutRecord(Day, 1, null, null)));
    }

    [Fact] public void Calculate_UsesTheRecords_AndDaysWithoutOneEarnFive()
    {
        var days = new[] { Day, Day.AddDays(1) };
        var awards = PointsCalculator.Calculate(days, [], null, [], Day.AddDays(1), Day,
            workouts: [new WorkoutRecord(Day.AddDays(1), 2, 4, null)]);
        Assert.Equal([5, 2], awards.Where(a => a.Reason == PointsReason.Workout).OrderBy(a => a.Date).Select(a => a.Amount));
    }
}

/// <summary>The live workout on in-memory SQLite: start, sets through SetService, the silent log at half, and how a
/// session ends.</summary>
public class LiveWorkoutServiceTests : IDisposable
{
    // 2026-10-05 is a Monday: Alice's Legs day (4 exercises, the first two with a warm-up); Tuesday is a rest day
    private static readonly DateOnly Mon = new(2026, 10, 5);

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private readonly TestDb _db = new();
    private readonly Clock _clock = new(new DateTime(2026, 10, 5, 18, 0, 0));
    private readonly WorkoutService _workouts;
    private readonly SetService _sets;
    private readonly LiveWorkoutService _svc;
    private readonly int _alice;
    private readonly int _legs, _arms;
    private readonly List<int> _ex;

    public LiveWorkoutServiceTests()
    {
        _alice = _db.AddUser("alice");
        var days = _db.AddSplit(_alice, ("Legs", DayOfWeek.Monday), ("Arms", DayOfWeek.Wednesday));
        (_legs, _arms) = (days[0], days[1]);
        _ex = _db.AddDayExercises(_alice, _legs, 4, warmupSets: 0, workingSets: 2);
        _db.AddDayExercises(_alice, _arms, 2);
        using (var db = _db.CreateDbContext())
        {
            foreach (var x in db.UserDayExercises.Where(x => x.SplitDayId == _legs && (x.ExerciseId == _ex[0] || x.ExerciseId == _ex[1])))
                x.WarmupSets = 1;
            db.UserSchedules.Add(new UserSchedule { UserId = _alice, EffectiveFrom = Mon, WeekPattern = "WRWRRRR" });
            db.SaveChanges();
        }
        var points = new PointsService(_db);
        _workouts = new WorkoutService(_db, new StreakService(_db), points);
        _sets = new SetService(_db, _workouts);
        _svc = new LiveWorkoutService(_db, _workouts, _sets, new StreakService(_db), _clock);
    }

    public void Dispose() => _db.Dispose();

    private static SetInput Warm => new(SetKind.Warmup, 10, 20);
    private static SetInput Work(double kg = 60, int reps = 8) => new(SetKind.Working, reps, kg);

    private async Task<LiveWorkoutInfo> Start()
    {
        var r = await _svc.StartAsync(_alice);
        Assert.True(r.Success, r.Error);
        return r.Workout!;
    }

    /// <summary>Logs every planned set of the exercise through the card.</summary>
    private async Task FillExercise(int exerciseId, bool warmup)
    {
        if (warmup) Assert.True((await _svc.SaveSetAsync(_alice, exerciseId, Warm)).Success);
        for (var i = 0; i < 2; i++) Assert.True((await _svc.SaveSetAsync(_alice, exerciseId, Work())).Success);
    }

    private List<WorkoutLog> Logs()
    {
        using var db = _db.CreateDbContext();
        return db.WorkoutLogs.AsNoTracking().Where(w => w.UserId == _alice).ToList();
    }

    private int WorkoutPoints()
    {
        using var db = _db.CreateDbContext();
        return db.PointsEntries.Where(p => p.UserId == _alice && p.Reason == PointsReason.Workout && p.Date == Mon).Sum(p => p.Amount);
    }

    private List<SetLog> Sets(int exerciseId)
    {
        using var db = _db.CreateDbContext();
        return db.SetLogs.AsNoTracking().Where(s => s.UserId == _alice && s.ExerciseId == exerciseId && s.Date == Mon)
            .OrderBy(s => s.SetNumber).ToList();
    }

    [Fact] public async Task Start_PicksTodaysPlannedDay()
    {
        var live = await Start();
        Assert.Equal(_legs, live.SplitDayId);
        Assert.Equal("Legs", live.DayName);
        Assert.Equal(_ex, live.Exercises.Select(e => e.ExerciseId));
        Assert.False(live.Logged);
    }

    [Fact] public async Task Start_Twice_KeepsOneOpenSession()
    {
        var first = await Start();
        var second = await Start();
        Assert.Equal(first.Id, second.Id);
        using var db = _db.CreateDbContext();
        Assert.Equal(1, db.LiveWorkouts.Count(l => l.UserId == _alice && l.EndedAt == null));
    }

    [Fact] public async Task OneOpenSessionPerUser_IsEnforcedByTheDatabase()
    {
        await Start();
        using var db = _db.CreateDbContext();
        db.LiveWorkouts.Add(new LiveWorkout { UserId = _alice, Date = Mon, SplitDayId = _legs, StartedAt = _clock.Now, LastActiveAt = _clock.Now });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact] public async Task Start_OnARestDay_AsksForTheDay_ThenStartsIt()
    {
        _clock.Now = new DateTime(2026, 10, 6, 18, 0, 0);
        var ask = await _svc.StartAsync(_alice);
        Assert.False(ask.Success);
        Assert.True(ask.NeedsDay);
        Assert.Equal(["Legs", "Arms"], ask.PickableDays!.Select(d => d.Name));

        var r = await _svc.StartAsync(_alice, _arms);
        Assert.True(r.Success);
        Assert.Equal(_arms, r.Workout!.SplitDayId);
    }

    [Fact] public async Task Start_WithoutExercises_IsRefused()
    {
        _clock.Now = new DateTime(2026, 10, 7, 18, 0, 0);   // Wednesday: Arms, which has exercises; remove them
        using (var db = _db.CreateDbContext())
        {
            db.UserDayExercises.RemoveRange(db.UserDayExercises.Where(x => x.SplitDayId == _arms));
            db.SaveChanges();
        }
        var r = await _svc.StartAsync(_alice);
        Assert.False(r.Success);
        Assert.Equal("You haven't built a workout for Arms yet.", r.Error);
    }

    [Fact] public async Task Sets_GoThroughSetService_WarmupsFirst()
    {
        await Start();
        await _svc.SaveSetAsync(_alice, _ex[0], Work(100, 5));
        await _svc.SaveSetAsync(_alice, _ex[0], Warm);   // a warm-up typed late still comes first
        var sets = Sets(_ex[0]);
        Assert.Equal([SetKind.Warmup, SetKind.Working], sets.Select(s => s.Kind));
        Assert.Equal([1, 2], sets.Select(s => s.SetNumber));
    }

    [Fact] public async Task Sets_KeepSetServiceLimits()
    {
        await Start();
        var r = await _svc.SaveSetAsync(_alice, _ex[0], Work(60, 0));
        Assert.False(r.Success);
        Assert.Equal($"Reps must be 1 to {SetService.MaxReps}.", r.Error);
        Assert.Empty(Sets(_ex[0]));
    }

    [Fact] public async Task ALoggedSet_CanBeChanged_InPlace()
    {
        await Start();
        await FillExercise(_ex[0], warmup: true);
        var r = await _svc.SaveSetAsync(_alice, _ex[0], Work(105, 6), replaceIndex: 1);
        Assert.True(r.Success);
        var sets = Sets(_ex[0]);
        Assert.Equal(3, sets.Count);
        Assert.Equal((SetKind.Working, 105.0, 6), (sets[1].Kind, sets[1].WeightKg, sets[1].Reps));
    }

    [Fact] public async Task TheLog_IsWrittenOnceAtHalf_ThenFollowsCompletion()
    {
        await Start();
        await FillExercise(_ex[0], warmup: true);
        Assert.Empty(Logs());

        await FillExercise(_ex[1], warmup: true);   // 2 of 4: half
        var log = Assert.Single(Logs());
        Assert.Equal((2, 4, false), (log.FilledExercises, log.TotalExercises, log.Verified));
        Assert.Equal(PointsCalculator.ReducedWorkoutPoints, WorkoutPoints());
        Assert.True((await _svc.GetOpenAsync(_alice))!.Logged);

        await FillExercise(_ex[2], warmup: false);   // 3 of 4: 70% or more, but not verified
        log = Assert.Single(Logs());
        Assert.Equal(3, log.FilledExercises);
        Assert.Equal(PointsCalculator.ReducedWorkoutPoints, WorkoutPoints());
    }

    // ===== Verified at gym (18c) =====

    private int AddGym(double lat, double lon, int? owner = null)
    {
        using var db = _db.CreateDbContext();
        var city = new City { OsmType = OsmElementType.Relation, OsmId = 7, Name = "Timișoara", DisplayName = "Timișoara" };
        db.Cities.Add(city);
        db.SaveChanges();
        var gym = new Gym { CityId = city.Id, Name = "Iron Gym", Latitude = lat, Longitude = lon, OwnerUserId = owner,
            OsmType = owner is null ? OsmElementType.Node : null, OsmId = owner is null ? 42 : null };
        db.Gyms.Add(gym);
        db.SaveChanges();
        return gym.Id;
    }

    [Fact] public async Task AtAGym_TheWorkoutIsVerified_AndTheLogGetsThatGym()
    {
        var gym = AddGym(45.7500, 21.2300);
        await Start();
        var live = await _svc.VerifyAsync(_alice, new LocationFix(45.7505, 21.2300, 20));   // ~56 m away
        Assert.Equal((gym, "Iron Gym"), (live!.VerifiedGymId, live.VerifiedGymName));

        await FillExercise(_ex[0], warmup: true);
        await FillExercise(_ex[1], warmup: true);
        var log = Assert.Single(Logs());
        Assert.Equal((true, gym), (log.Verified, log.GymId));
        Assert.Equal(PointsCalculator.ReducedWorkoutPoints, WorkoutPoints());   // verified, but only half
        await FillExercise(_ex[2], warmup: false);
        Assert.Equal(PointsCalculator.WorkoutPoints, WorkoutPoints());   // verified with 70%: the full 5
    }

    [Fact] public async Task VerifiedAfterTheSilentLog_UpdatesTheLog()
    {
        var gym = AddGym(45.75, 21.23);
        await Start();
        await FillExercise(_ex[0], warmup: true);
        await FillExercise(_ex[1], warmup: true);
        await FillExercise(_ex[2], warmup: false);
        Assert.Equal(PointsCalculator.ReducedWorkoutPoints, WorkoutPoints());

        await _svc.VerifyAsync(_alice, new LocationFix(45.75, 21.23, 10));
        var log = Assert.Single(Logs());
        Assert.Equal((true, gym), (log.Verified, log.GymId));
        Assert.Equal(PointsCalculator.WorkoutPoints, WorkoutPoints());
    }

    [Fact] public async Task AwayFromEveryGym_OrWithAPoorFix_StaysUnverified()
    {
        AddGym(45.75, 21.23);
        await Start();
        Assert.False((await _svc.VerifyAsync(_alice, new LocationFix(45.76, 21.23, 20)))!.IsVerified);    // ~1.1 km
        Assert.False((await _svc.VerifyAsync(_alice, new LocationFix(45.75, 21.23, 500)))!.IsVerified);   // too vague
        using var db = _db.CreateDbContext();
        Assert.Null(db.LiveWorkouts.Single().VerifiedGymId);
    }

    [Fact] public async Task AnotherUsersCustomGym_DoesntCount()
    {
        var bob = _db.AddUser("bob");
        AddGym(45.75, 21.23, owner: bob);
        await Start();
        Assert.False((await _svc.VerifyAsync(_alice, new LocationFix(45.75, 21.23, 10)))!.IsVerified);
    }

    [Fact] public async Task Finish_Verified_EarnsTheFullPoints()
    {
        AddGym(45.75, 21.23);
        await Start();
        await _svc.VerifyAsync(_alice, new LocationFix(45.75, 21.23, 10));
        foreach (var (e, i) in _ex.Select((e, i) => (e, i))) await FillExercise(e, warmup: i < 2);
        var r = await _svc.FinishAsync(_alice);
        Assert.Equal((LiveOutcome.Full, PointsCalculator.WorkoutPoints), (r.Outcome, r.WorkoutPoints));
    }

    [Fact] public async Task AFilledPickedExercise_HandsTheCardBackToThePlan()
    {
        await Start();
        await _svc.PickAsync(_alice, _ex[2]);
        Assert.Equal(_ex[2], (await _svc.GetOpenAsync(_alice))!.CurrentExerciseId);
        await FillExercise(_ex[2], warmup: false);
        Assert.Null((await _svc.GetOpenAsync(_alice))!.CurrentExerciseId);
    }

    [Fact] public async Task Skip_MovesTheExerciseBehindTheOthers()
    {
        await Start();
        var live = await _svc.SkipAsync(_alice, _ex[0]);
        Assert.Equal([_ex[0]], live!.SkippedExerciseIds);
        Assert.Equal(_ex[1], LiveWorkoutRules.Current(live.Plan, live.CurrentExerciseId, live.SkippedExerciseIds)!.ExerciseId);
    }

    [Fact] public async Task End_BelowHalf_LogsNothing_AndKeepsTheSets()
    {
        await Start();
        await FillExercise(_ex[0], warmup: true);
        var r = await _svc.EndAsync(_alice);
        Assert.True(r.Success);
        Assert.Equal(LiveOutcome.NotCounted, r.Outcome);
        Assert.Empty(Logs());
        Assert.Equal(3, Sets(_ex[0]).Count);
        Assert.Null(await _svc.GetOpenAsync(_alice));
    }

    [Fact] public async Task Finish_BelowHalf_IsRefused()
    {
        await Start();
        await FillExercise(_ex[0], warmup: true);
        var r = await _svc.FinishAsync(_alice);
        Assert.False(r.Success);
        Assert.NotNull(await _svc.GetOpenAsync(_alice));
    }

    [Fact] public async Task Finish_SavesTheCardio_AndEndsTheSession()
    {
        await Start();
        await FillExercise(_ex[0], warmup: true);
        await FillExercise(_ex[1], warmup: true);
        var r = await _svc.FinishAsync(_alice, cardioMinutes: 25);
        Assert.True(r.Success, r.Error);
        Assert.Equal((LiveOutcome.Half, 2, 4, 2, 2), (r.Outcome, r.Filled, r.Total, r.WorkoutPoints, r.CardioPoints));
        Assert.Equal(25, Assert.Single(Logs()).CardioMinutes);
        Assert.Null(await _svc.GetOpenAsync(_alice));
        Assert.NotNull(r.Log!.Streak);
    }

    [Fact] public async Task AfterUndo_ANewWorkoutCanStartTheSameDay()
    {
        await Start();
        await FillExercise(_ex[0], warmup: true);
        await FillExercise(_ex[1], warmup: true);
        await _svc.FinishAsync(_alice);
        await _workouts.UndoTodayAsync(_alice, Mon);

        var again = await Start();
        Assert.False(again.Logged);
        Assert.Equal(2, again.Status.Filled);   // the sets stay
    }

    [Fact] public async Task ASession_StopsAfterFourHours()
    {
        await Start();
        _clock.Now = _clock.Now.AddHours(4);
        Assert.False(await _svc.IsOpenAsync(_alice));
        Assert.Null(await _svc.GetOpenAsync(_alice));
        using var db = _db.CreateDbContext();
        Assert.NotNull(db.LiveWorkouts.Single(l => l.UserId == _alice).EndedAt);
    }

    [Fact] public async Task ASession_StopsAtMidnight()
    {
        _clock.Now = new DateTime(2026, 10, 5, 23, 0, 0);
        await Start();
        _clock.Now = new DateTime(2026, 10, 6, 0, 1, 0);
        Assert.Null(await _svc.GetOpenAsync(_alice));
    }
}

/// <summary>Today's log follows today's sets wherever they're saved: the Day and Exercise screens, the desktop check-in.</summary>
public class WorkoutCompletionTests : IDisposable
{
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private readonly TestDb _db = new();
    private readonly WorkoutService _workouts;
    private readonly SetService _sets;
    private readonly int _alice;
    private readonly List<int> _ex;

    public WorkoutCompletionTests()
    {
        _alice = _db.AddUser("alice");
        var day = _db.AddSplit(_alice, ("Push", DayOfWeek.Monday))[0];
        _ex = _db.AddDayExercises(_alice, day, 4, workingSets: 1);
        _workouts = new WorkoutService(_db, new StreakService(_db), new PointsService(_db));
        _sets = new SetService(_db, _workouts);
    }

    public void Dispose() => _db.Dispose();

    private Task<SetResult> Fill(int exerciseId) => _sets.SaveDayAsync(_alice, exerciseId, [new SetInput(SetKind.Working, 8, 60)], Mon);

    private (int? Filled, int? Total) Completion()
    {
        using var db = _db.CreateDbContext();
        var log = db.WorkoutLogs.Single(w => w.UserId == _alice);
        return (log.FilledExercises, log.TotalExercises);
    }

    private int WorkoutPoints()
    {
        using var db = _db.CreateDbContext();
        return db.PointsEntries.Where(p => p.UserId == _alice && p.Reason == PointsReason.Workout).Sum(p => p.Amount);
    }

    [Fact] public async Task TheCheckIn_StoresCompletion_AndIsNeverVerified()
    {
        foreach (var e in _ex) await Fill(e);
        Assert.True((await _workouts.LogTodayAsync(_alice, today: Mon)).Success);
        Assert.Equal((4, 4), Completion());
        using (var db = _db.CreateDbContext()) Assert.False(db.WorkoutLogs.Single().Verified);
        Assert.Equal(PointsCalculator.ReducedWorkoutPoints, WorkoutPoints());   // the desktop earns 2 even for all of it
    }

    [Fact] public async Task SetsSavedLater_UpdateTheLog_AndItsPoints()
    {
        await Fill(_ex[0]);
        await Fill(_ex[1]);
        await _workouts.LogTodayAsync(_alice, today: Mon);
        using (var db = _db.CreateDbContext())
        {
            db.WorkoutLogs.Single().Verified = true;   // as a verified live workout writes it
            db.SaveChanges();
        }
        await new PointsService(_db).SyncAsync(_alice, Mon);
        Assert.Equal(PointsCalculator.ReducedWorkoutPoints, WorkoutPoints());

        await Fill(_ex[2]);   // the Day screen
        Assert.Equal((3, 4), Completion());
        Assert.Equal(PointsCalculator.WorkoutPoints, WorkoutPoints());

        Assert.True((await _sets.SaveDayAsync(_alice, _ex[2], [], Mon)).Success);   // removed again
        Assert.Equal((2, 4), Completion());
        Assert.Equal(PointsCalculator.ReducedWorkoutPoints, WorkoutPoints());
    }

    [Fact] public async Task ALogFromBefore018_KeepsItsFivePoints()
    {
        using (var db = _db.CreateDbContext())
        {
            db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = Mon, SplitDayId = null });
            db.SaveChanges();
        }
        await new PointsService(_db).RebuildAsync(_alice, Mon);
        Assert.Equal(PointsCalculator.WorkoutPoints, WorkoutPoints());
    }
}

/// <summary>The card on a bar exercise: per side plus the bar saves the total, and the mode locks after the first set.</summary>
public class LiveWorkoutCardBarTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly int _alice;
    private readonly int _squat;
    private readonly LiveWorkoutService _live;
    private readonly SetService _sets;

    public LiveWorkoutCardBarTests()
    {
        _alice = _db.AddUser("alice");
        var day = _db.AddSplit(_alice, (AppClock.Today.DayOfWeek.ToString(), AppClock.Today.DayOfWeek))[0];
        using (var db = _db.CreateDbContext())
        {
            _squat = db.Exercises.Where(e => e.OwnerUserId == null).AsEnumerable().First(e => GymApp.Core.BarWeight.UsesBar(e.Name)).Id;
            db.UserDayExercises.Add(new UserDayExercise { UserId = _alice, SplitDayId = day, ExerciseId = _squat, Order = 1, WorkingSets = 2 });
            db.SaveChanges();
        }
        var workouts = new WorkoutService(_db, new StreakService(_db), new PointsService(_db));
        _sets = new SetService(_db, workouts);
        _live = new LiveWorkoutService(_db, workouts, _sets, new StreakService(_db));
    }

    public void Dispose() => _db.Dispose();

    [Fact] public async Task PerSide_SavesTheTotal_ThenTheModeLocks()
    {
        await _sets.SaveBarSettingAsync(_alice, _squat, new BarSetting(true, 20));
        var start = await _live.StartAsync(_alice);
        Assert.True(start.Success, start.Error);
        var card = new GymApp.App.ViewModels.LiveWorkoutCardViewModel(_live, _sets, GymApp.App.NoLocationProvider.Instance, _alice, start.Workout!, _ => { }, _ => Task.CompletedTask, () => { });
        for (var i = 0; i < 20 && !card.IsPerSide; i++) await Task.Delay(20);   // the bar settings load in the background

        Assert.True(card.UsesBar);
        Assert.True(card.IsPerSide);
        Assert.False(card.IsBarLocked);
        card.WeightText = "60";
        card.RepsText = "5";
        Assert.Equal("= 140 kg (60 × 2 + 20 bar)", card.TotalHint);
        await card.NextCommand.ExecuteAsync(null);

        Assert.Equal(140, Assert.Single(await _sets.GetDayAsync(_alice, _squat)).WeightKg);
        Assert.True(card.IsBarLocked);
        Assert.Equal("Per side · 20 kg bar", card.LockedModeText);
        card.SetTotalModeCommand.Execute(null);
        Assert.True(card.IsPerSide);   // locked: no switching mid-exercise
        Assert.Equal("60", card.WeightText);   // the next set starts from the last one, per side
    }
}

/// <summary>Phase 18c's rule: near one of the user's gyms, within its radius plus the fix's accuracy.</summary>
public class GymProximityTests
{
    private static readonly GymSpot Osm = new(1, "Iron Gym", 45.7500, 21.2300, IsCustom: false);
    private static readonly GymSpot Custom = new(2, "Garage", 45.7600, 21.2300, IsCustom: true);

    // 0.001° of latitude is about 111 m
    private static LocationFix At(double lat, double lon = 21.2300, double accuracy = 0) => new(lat, lon, accuracy);

    [Fact] public void Inside_TheRadius_IsThatGym() => Assert.Equal(Osm, GymProximity.Nearest(At(45.7510), [Osm, Custom]));

    [Fact] public void Outside_IsNoGym() => Assert.Null(GymProximity.Nearest(At(45.7550), [Osm, Custom]));   // ~555 m from both

    [Fact] public void TheAccuracy_WidensTheRadius()
    {
        Assert.Null(GymProximity.Nearest(At(45.7516), [Osm]));                          // ~178 m: past 150
        Assert.Equal(Osm, GymProximity.Nearest(At(45.7516, accuracy: 40), [Osm]));      // 150 + 40
    }

    [Fact] public void CustomGyms_Reach250Metres()
    {
        Assert.Equal(Custom, GymProximity.Nearest(At(45.7620), [Custom]));   // ~222 m
        Assert.Null(GymProximity.Nearest(At(45.7625), [Custom]));            // ~278 m
    }

    [Fact] public void APoorFix_IsIgnored()
    {
        Assert.Null(GymProximity.Nearest(At(45.7500, accuracy: 201), [Osm]));
        Assert.Equal(Osm, GymProximity.Nearest(At(45.7500, accuracy: 200), [Osm]));
        Assert.Null(GymProximity.Nearest(new LocationFix(double.NaN, 21, 5), [Osm]));
    }

    [Fact] public void TheNearestWins() =>
        Assert.Equal(Custom, GymProximity.Nearest(At(45.7590, accuracy: 200), [Osm, Custom]));

    [Fact] public void Haversine_IsAboutRight() =>
        Assert.InRange(GymProximity.DistanceMeters(45.75, 21.23, 45.76, 21.23), 1110, 1113);

    [Theory]
    [InlineData(true, 4, 4, "Workout at Iron Gym")]
    [InlineData(true, 2, 4, "Workout at Iron Gym (partial)")]
    [InlineData(false, 4, 4, "Workout (not verified)")]
    [InlineData(false, 2, 4, "Workout (not verified, partial)")]
    [InlineData(null, null, null, "Workout")]
    public void TheHistoryLabel(bool? verified, int? filled, int? total, string label) =>
        Assert.Equal(label, PointsLine.ForWorkout(new WorkoutRecord(new DateOnly(2026, 10, 5), filled, total, verified),
            verified == true ? "Iron Gym" : null));
}

/// <summary>The card's location lines (18c): approximate location, a vague fix, and a fix away from every gym.</summary>
public class LiveWorkoutCardLocationTests : IDisposable
{
    private sealed class FakeLocation(bool precise, LocationFix? fix) : GymApp.App.ILocationProvider
    {
        public int Reads { get; private set; }
        public bool IsSupported => true;
        public bool HasPermission => true;
        public bool IsPrecise => precise;
        public bool NeedsNoteBeforePrompt => false;
        public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
        public Task<LocationFix?> GetFixAsync(TimeSpan timeout) { Reads++; return Task.FromResult(fix); }
        public void OpenSettings() { }
    }

    private readonly TestDb _db = new();
    private readonly int _alice;
    private readonly LiveWorkoutService _live;
    private readonly SetService _sets;

    public LiveWorkoutCardLocationTests()
    {
        _alice = _db.AddUser("alice");
        var day = _db.AddSplit(_alice, (AppClock.Today.DayOfWeek.ToString(), AppClock.Today.DayOfWeek))[0];
        _db.AddDayExercises(_alice, day, 2);
        var workouts = new WorkoutService(_db, new StreakService(_db), new PointsService(_db));
        _sets = new SetService(_db, workouts);
        _live = new LiveWorkoutService(_db, workouts, _sets, new StreakService(_db));
    }

    public void Dispose() => _db.Dispose();

    private async Task<GymApp.App.ViewModels.LiveWorkoutCardViewModel> Card(FakeLocation location)
    {
        var start = await _live.StartAsync(_alice);
        var card = new GymApp.App.ViewModels.LiveWorkoutCardViewModel(_live, _sets, location, _alice, start.Workout!, _ => { },
            _ => Task.CompletedTask, () => { });
        for (var i = 0; i < 10; i++) await Task.Delay(20);   // the first check runs in the background
        return card;
    }

    [Fact] public async Task ApproximateLocation_SaysToTurnOnPrecise_AndIsntRead()
    {
        var location = new FakeLocation(precise: false, new LocationFix(45.75, 21.23, 1500));
        var card = await Card(location);
        Assert.True(card.IsPreciseOff);
        Assert.False(card.IsNotAtGym);
        Assert.Equal(0, location.Reads);
    }

    [Fact] public async Task AVagueFix_IsntTakenForBeingElsewhere()
    {
        var card = await Card(new FakeLocation(precise: true, new LocationFix(30, -40, 500)));
        Assert.False(card.IsNotAtGym);
        Assert.True(card.IsLocationPending);
    }

    [Fact] public async Task AGoodFixAwayFromEveryGym_SaysSo()
    {
        var card = await Card(new FakeLocation(precise: true, new LocationFix(30, -40, 15)));
        Assert.True(card.IsNotAtGym);
        Assert.False(card.IsPreciseOff);
    }
}
