using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class StreakServiceTests : IDisposable
{
    // 2026-10-05 is a Monday. PPL from that Monday: Mon W, Tue W, Wed W, Thu R, Fri W, Sat W, Sun W
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int n) => Mon.AddDays(n);

    private readonly TestDb _db = new();
    private readonly StreakService _svc;
    private readonly int _alice;

    public StreakServiceTests()
    {
        _svc = new StreakService(_db);
        _alice = _db.AddUser("alice");
        Seed(db => db.UserSchedules.Add(new UserSchedule { UserId = _alice, EffectiveFrom = Mon, WeekPattern = "WWWRWWW" }));
        // Mon logged, Tue missed, Wed logged
        Seed(db => db.WorkoutLogs.AddRange(new WorkoutLog { UserId = _alice, Date = D(0) }, new WorkoutLog { UserId = _alice, Date = D(2) }));
    }

    public void Dispose() => _db.Dispose();

    private void Seed(Action<AppDbContext> change)
    {
        using var db = _db.CreateDbContext();
        change(db);
        db.SaveChanges();
    }

    private void Choose(MissChoice choice) =>
        Seed(db => db.MissedDayChoices.Add(new MissedDayChoice { UserId = _alice, Date = D(1), Choice = choice }));

    private int CachedStreak()
    {
        using var db = _db.CreateDbContext();
        return db.Users.AsNoTracking().Single(u => u.Id == _alice).CurrentStreak;
    }

    [Fact] public async Task NoChoiceStored_ActsLikeShiftToRest()
    {
        var result = await _svc.RecalculateAsync(_alice, D(2));
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Shifted, D(2)), result);
    }

    [Theory]
    [InlineData(MissChoice.ShiftToRest, 3, StreakStatus.Shifted)]
    [InlineData(MissChoice.ShiftWeek, 3, StreakStatus.Active)]
    [InlineData(MissChoice.Continue, 1, StreakStatus.Active)]
    public async Task StoredChoice_ChangesTheResultForTheSameLogs(MissChoice choice, int current, StreakStatus status)
    {
        Choose(choice);
        var result = await _svc.RecalculateAsync(_alice, D(2));
        Assert.Equal(current, result.Current);
        Assert.Equal(status, result.Status);
        Assert.Equal(current, CachedStreak());
    }

    [Fact] public async Task OtherUsersChoices_AreNotUsed()
    {
        var bob = _db.AddUser("bob");
        Seed(db => db.MissedDayChoices.Add(new MissedDayChoice { UserId = bob, Date = D(1), Choice = MissChoice.Continue }));
        Assert.Equal(StreakStatus.Shifted, (await _svc.RecalculateAsync(_alice, D(2))).Status);
    }

    [Fact] public async Task Continue_MakesTheWeekMissed_ForPoints()
    {
        // ShiftToRest chain paid on Thu: the week can still earn the grace bonus. Continue: Tue is Missed, so it can't
        Choose(MissChoice.Continue);
        var points = new PointsService(_db);
        Seed(db => db.WorkoutLogs.AddRange(Enumerable.Range(3, 4).Select(n => new WorkoutLog { UserId = _alice, Date = D(n) })));
        await points.RebuildAsync(_alice, D(7));
        using var db = _db.CreateDbContext();
        Assert.DoesNotContain(db.PointsEntries.Where(p => p.UserId == _alice), p => p.DedupKey == "week:2026-10-05");
    }

    [Fact] public async Task ShiftToRest_PaidChain_EarnsTheGraceBonus()
    {
        Choose(MissChoice.ShiftToRest);
        var points = new PointsService(_db);
        Seed(db => db.WorkoutLogs.AddRange(Enumerable.Range(3, 4).Select(n => new WorkoutLog { UserId = _alice, Date = D(n) })));
        await points.RebuildAsync(_alice, D(7));
        using var db = _db.CreateDbContext();
        var week = db.PointsEntries.Single(p => p.UserId == _alice && p.DedupKey == "week:2026-10-05");
        Assert.Equal(PointsReason.WeekCompleteGrace, week.Reason);
    }

    // ===== Rest-day rule (0.18.1) =====

    private DateOnly? RuleFrom()
    {
        using var db = _db.CreateDbContext();
        return db.Users.AsNoTracking().Single(u => u.Id == _alice).StreakRestRuleFrom;
    }

    [Fact] public async Task RestRule_StartsOnTheFirstRecalculation_AndKeepsThatDay()
    {
        Assert.Null(RuleFrom());
        await _svc.RecalculateAsync(_alice, D(2));
        Assert.Equal(D(2), RuleFrom());
        await _svc.RecalculateAsync(_alice, D(3));
        Assert.Equal(D(2), RuleFrom());
    }

    [Fact] public async Task RestRule_RestDayAfterTheRule_IsStale_UntilAVerifiedWorkout()
    {
        await _svc.RecalculateAsync(_alice, D(2));   // the rule starts Wed: Mon 1, Tue 2 (provisional), Wed pays it -> 3
        // Thu (rest) pays Wed's shifted day with an unverified workout: kept at 3
        Seed(db => db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = D(3) }));
        Assert.Equal(new StreakResult(3, 3, StreakStatus.Active), await _svc.RecalculateAsync(_alice, D(3)));

        // Next Thu (rest) after a full week: on ice, until a verified workout adds a day
        Seed(db => db.WorkoutLogs.AddRange(new[] { 4, 5, 6, 7, 8, 9 }.Select(n => new WorkoutLog { UserId = _alice, Date = D(n) })));
        var thu = await _svc.RecalculateAsync(_alice, D(10));
        Assert.True(thu.Stale);
        Assert.Equal(9, thu.Current);

        Seed(db => db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = D(10), Verified = true }));
        var verified = await _svc.RecalculateAsync(_alice, D(10));
        Assert.False(verified.Stale);
        Assert.Equal(10, verified.Current);
    }
}
