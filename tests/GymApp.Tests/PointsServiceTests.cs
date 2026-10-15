using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class PointsServiceTests : IDisposable
{
    // 2026-10-05 is a Monday. D(0) = Mon ... D(7) = next Mon
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int n) => Mon.AddDays(n);

    private readonly TestDb _db = new();
    private readonly PointsService _svc;
    private readonly int _alice;
    private readonly int _bob;

    public PointsServiceTests()
    {
        _svc = new PointsService(_db);
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
        // Upper/Lower from the first Monday: Mon, Tue, Thu, Fri training
        Seed(db => db.UserSchedules.Add(new UserSchedule { UserId = _alice, EffectiveFrom = Mon, WeekPattern = "WWRWWRR" }));
    }

    public void Dispose() => _db.Dispose();

    private void Seed(Action<AppDbContext> change)
    {
        using var db = _db.CreateDbContext();
        change(db);
        db.SaveChanges();
    }

    private void Workouts(int userId, params int[] days) =>
        Seed(db => db.WorkoutLogs.AddRange(days.Select(d => new WorkoutLog { UserId = userId, Date = D(d) })));

    private void Calories(int userId, int day, int consumed, int goal = 1800) =>
        Seed(db => db.CalorieLogs.Add(new CalorieLog
            { UserId = userId, Date = D(day), CaloriesConsumed = consumed, GoalAtTime = goal, GoalTypeAtTime = GoalType.Lose }));

    private List<PointsEntry> Entries(int userId)
    {
        using var db = _db.CreateDbContext();
        return db.PointsEntries.AsNoTracking().Where(p => p.UserId == userId).OrderBy(p => p.DedupKey).ToList();
    }

    private User User(int userId)
    {
        using var db = _db.CreateDbContext();
        return db.Users.AsNoTracking().Single(u => u.Id == userId);
    }

    private void AssertCachedSum(int userId) =>
        Assert.Equal(Entries(userId).Sum(p => p.Amount), User(userId).Points);

    [Fact] public async Task SyncTwice_ChangesNothing()
    {
        Workouts(_alice, 0, 1, 3, 4);
        Calories(_alice, 2, 1800);

        var first = await _svc.SyncAsync(_alice, D(5));
        Assert.Equal(4 * 5 + 50 + 10, first!.NewTotal);
        var before = Entries(_alice).Select(p => (p.Id, p.Reason, p.Amount, p.Date, p.DedupKey)).ToList();

        var second = await _svc.SyncAsync(_alice, D(5));
        Assert.Empty(second!.Added);
        Assert.Equal(first.NewTotal, second.OldTotal);
        Assert.Equal(first.NewTotal, second.NewTotal);
        Assert.Equal(before, Entries(_alice).Select(p => (p.Id, p.Reason, p.Amount, p.Date, p.DedupKey)));
    }

    [Fact] public async Task Sync_ReportsAddedEntriesAndLevelUp()
    {
        // 95 points already, frozen two weeks back
        Seed(db => db.PointsEntries.Add(new PointsEntry
            { UserId = _alice, Reason = PointsReason.Workout, Amount = 95, Date = D(-14), DedupKey = "workout:2026-09-21" }));
        Assert.Empty((await _svc.RebuildAsync(_alice, D(0)))!.Added);

        Workouts(_alice, 0);
        var r = await _svc.SyncAsync(_alice, D(0));
        Assert.Equal(95, r!.OldTotal);
        Assert.Equal(100, r.NewTotal);
        Assert.Equal(PointsReason.Workout, Assert.Single(r.Added).Reason);
        Assert.True(r.LeveledUp);
        Assert.Equal("Novice", r.NewLevel.Name);
    }

    [Fact] public async Task SourceChangeInTheWindow_RemovesTheEntry_AndLowersPoints()
    {
        Workouts(_alice, 0, 1, 3, 4);
        await _svc.SyncAsync(_alice, D(5));
        Assert.Equal(70, User(_alice).Points);

        Seed(db => db.WorkoutLogs.RemoveRange(db.WorkoutLogs.Where(w => w.UserId == _alice && w.Date == D(4))));
        var r = await _svc.SyncAsync(_alice, D(5));

        // Friday's +5 and the week's +50 are gone; a drop adds nothing
        Assert.Equal(15, r!.NewTotal);
        Assert.Empty(r.Added);
        Assert.DoesNotContain(Entries(_alice), p => p.DedupKey is "workout:2026-10-09" or "week:2026-10-05");
        AssertCachedSum(_alice);
    }

    [Fact] public async Task EntriesBeforeTheWindow_AreNeverDeletedOrChanged()
    {
        Workouts(_alice, 0, 1, 3, 4);
        await _svc.SyncAsync(_alice, D(5));
        var frozen = Entries(_alice);

        // Two weeks later the first week is outside the window (it starts on the previous Monday, D(14) - 7)
        Seed(db => db.WorkoutLogs.RemoveRange(db.WorkoutLogs.Where(w => w.UserId == _alice)));
        Seed(db => db.UserSchedules.Single(s => s.UserId == _alice).WeekPattern = "WWWWWWW");
        await _svc.SyncAsync(_alice, D(15));

        Assert.Equal(frozen.Select(p => (p.Id, p.Amount, p.Date, p.DedupKey)),
            Entries(_alice).Select(p => (p.Id, p.Amount, p.Date, p.DedupKey)));
        Assert.Equal(70, User(_alice).Points);
    }

    [Fact] public async Task FrozenEntries_StillCountInTheTotal()
    {
        Seed(db => db.PointsEntries.Add(new PointsEntry
            { UserId = _alice, Reason = PointsReason.WeekComplete, Amount = 50, Date = D(-10), DedupKey = "week:2026-09-21" }));
        await _svc.RebuildAsync(_alice, D(0));
        Workouts(_alice, 0);

        Assert.Equal(55, (await _svc.SyncAsync(_alice, D(0)))!.NewTotal);
        AssertCachedSum(_alice);
    }

    [Fact] public async Task Rebuild_CoversOldData_SetsTheFlag_AndRunsOnlyOnce()
    {
        // Old history from three weeks back, before any sync ran
        Workouts(_alice, 0, 1, 3, 4);
        Calories(_alice, 0, 1800);
        Assert.False(User(_alice).PointsBackfilled);

        await _svc.BackfillAllAsync(D(21));
        Assert.True(User(_alice).PointsBackfilled);
        Assert.True(User(_bob).PointsBackfilled);
        Assert.Equal(4 * 5 + 50 + 10, User(_alice).Points);
        Assert.Equal(0, User(_bob).Points);

        // A second backfill doesn't run again: the old entries stay even though their logs are gone
        Seed(db => db.WorkoutLogs.RemoveRange(db.WorkoutLogs.Where(w => w.UserId == _alice)));
        await _svc.BackfillAllAsync(D(21));
        Assert.Equal(80, User(_alice).Points);
    }

    [Fact] public async Task Sync_RebuildsAUserThatWasNeverBackfilled()
    {
        Workouts(_alice, 0);   // weeks before the window
        var r = await _svc.SyncAsync(_alice, D(21));
        Assert.Equal(5, r!.NewTotal);
        Assert.True(User(_alice).PointsBackfilled);
    }

    [Fact] public async Task LoggingSets_NeverCreatesAnEntry()
    {
        await _svc.RebuildAsync(_alice, D(0));
        var sets = new SetService(_db);
        Assert.True((await sets.SaveDayAsync(_alice, 1, [new SetInput(SetKind.Working, 10, 60)], D(0))).Success);
        Assert.True((await sets.SaveDayAsync(_alice, 1, [new SetInput(SetKind.Working, 10, 100)], D(0))).Success);   // a PR

        Assert.Empty(Entries(_alice));
        await _svc.SyncAsync(_alice, D(0));
        Assert.Empty(Entries(_alice));
        Assert.Equal(0, User(_alice).Points);
    }

    [Fact] public async Task HooksInTheServices_KeepPointsInStep()
    {
        var workouts = new WorkoutService(_db, new StreakService(_db), _svc);
        var log = await workouts.LogTodayAsync(_alice);
        Assert.True(log.Success);
        Assert.Contains(log.Points!.Added, a => a.Reason == PointsReason.Workout);
        AssertCachedSum(_alice);
    }

    [Fact] public async Task CachedPoints_AlwaysEqualTheSumOfEntries()
    {
        Workouts(_alice, 0, 1);
        await _svc.SyncAsync(_alice, D(1));
        AssertCachedSum(_alice);

        Workouts(_alice, 3, 4);
        Calories(_alice, 4, 1700);
        await _svc.SyncAsync(_alice, D(4));
        AssertCachedSum(_alice);

        Seed(db => db.CalorieLogs.Single(c => c.UserId == _alice).CaloriesConsumed = 900);   // no longer a hit
        await _svc.SyncAsync(_alice, D(4));
        AssertCachedSum(_alice);
        Assert.Equal(70, User(_alice).Points);

        // other users are untouched
        Assert.Empty(Entries(_bob));
        Assert.Equal(0, User(_bob).Points);
    }

    [Fact] public async Task UniqueIndex_RejectsADuplicateKey()
    {
        var entry = () => new PointsEntry
            { UserId = _alice, Reason = PointsReason.Workout, Amount = 5, Date = D(0), DedupKey = "workout:2026-10-05" };
        Seed(db => db.PointsEntries.Add(entry()));
        Assert.Throws<DbUpdateException>(() => Seed(db => db.PointsEntries.Add(entry())));
    }
}
