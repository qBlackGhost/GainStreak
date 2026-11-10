using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class VacationServiceTests : IDisposable
{
    // 2026-10-05 is a Monday. PPL from that Monday: Mon W, Tue W, Wed W, Thu R, Fri W, Sat W, Sun W.
    // Alice logged Mon and Tue; Wed (today in most tests) isn't logged yet.
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int n) => Mon.AddDays(n);

    private readonly TestDb _db = new();
    private readonly VacationService _svc;
    private readonly StreakService _streaks;
    private readonly int _alice;

    public VacationServiceTests()
    {
        _streaks = new StreakService(_db);
        _svc = new VacationService(_db, _streaks, new PointsService(_db));
        _alice = _db.AddUser("alice");
        Seed(db =>
        {
            db.UserSchedules.Add(new UserSchedule { UserId = _alice, EffectiveFrom = Mon, WeekPattern = "WWWRWWW" });
            db.WorkoutLogs.AddRange(new WorkoutLog { UserId = _alice, Date = D(0) }, new WorkoutLog { UserId = _alice, Date = D(1) });
        });
    }

    public void Dispose() => _db.Dispose();

    private void Seed(Action<AppDbContext> change)
    {
        using var db = _db.CreateDbContext();
        change(db);
        db.SaveChanges();
    }

    private void Log(int day) => Seed(db => db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = D(day) }));

    private async Task<DateOnly[]> Frozen() => (await _svc.GetFrozenDatesAsync(_alice)).Order().ToArray();

    [Fact]
    public async Task Start_FreezesFromToday_AndRecalculates()
    {
        var r = await _svc.StartAsync(_alice, D(5), D(2));

        Assert.True(r.Success);
        Assert.Equal([D(2), D(3), D(4)], await Frozen());
        Assert.Equal(new StreakResult(2, 2, StreakStatus.Active, Stale: true, VacationUntil: D(5)), r.Streak);
        using var db = _db.CreateDbContext();
        Assert.Equal(2, db.Users.Single(u => u.Id == _alice).CurrentStreak);
    }

    [Fact]
    public async Task Start_AfterTodaysWorkout_BeginsTomorrow()
    {
        Log(2);
        Assert.Equal(D(3), await _svc.StartDateAsync(_alice, D(2)));
        Assert.False((await _svc.StartAsync(_alice, D(3), D(2))).Success);    // must be after the start

        Assert.True((await _svc.StartAsync(_alice, D(5), D(2))).Success);
        Assert.Equal([D(3), D(4)], await Frozen());
        Assert.Equal(3, (await _streaks.GetAsync(_alice, D(2))).Current);      // today's workout still counts
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public async Task Start_ReturnDateOutOfRange_IsRefused(int returnDay)
    {
        var r = await _svc.StartAsync(_alice, D(returnDay), D(2));
        Assert.False(r.Success);
        Assert.Empty(await Frozen());
    }

    [Fact]
    public async Task Start_DuringTheCooldown_IsRefused_WithTheDate()
    {
        Assert.True((await _svc.StartAsync(_alice, D(3), D(2))).Success);

        var refused = await _svc.StartAsync(_alice, D(62), D(61));
        Assert.False(refused.Success);
        Assert.Equal(VacationService.RefusalText(new VacationCheck(VacationRefusal.Cooldown, D(62))), refused.Message);
        Assert.True((await _svc.StartAsync(_alice, D(63), D(62))).Success);
    }

    [Fact]
    public async Task Start_WithAMissedDayToChooseFor_IsRefused()
    {
        var bob = _db.AddUser("bob");
        Seed(db =>
        {
            db.UserSchedules.Add(new UserSchedule { UserId = bob, EffectiveFrom = Mon, WeekPattern = "WWWRWWW" });
            db.WorkoutLogs.Add(new WorkoutLog { UserId = bob, Date = D(0) });   // Tue missed: on Wed he must choose
        });

        var state = await _svc.GetStateAsync(bob, D(2));
        Assert.Equal(VacationRefusal.SettleMissedDay, state.Check.Refusal);
        Assert.False((await _svc.StartAsync(bob, D(4), D(2))).Success);
    }

    [Fact]
    public async Task EndEarly_TodayStaysFrozen_TomorrowCountsAgain()
    {
        await _svc.StartAsync(_alice, D(9), D(2));

        var r = await _svc.EndEarlyAsync(_alice, D(3));

        Assert.True(r.Success);
        Assert.Equal([D(2), D(3)], await Frozen());
        Assert.Equal(StreakStatus.PendingToday, (await _streaks.GetAsync(_alice, D(4))).Status);
        Assert.Equal(VacationRefusal.Cooldown, (await _svc.CanStartAsync(_alice, D(4))).Refusal);
    }

    [Fact]
    public async Task EndEarly_BeforeItStarts_CancelsIt()
    {
        Log(2);
        await _svc.StartAsync(_alice, D(5), D(2));   // starts Thu

        Assert.True((await _svc.EndEarlyAsync(_alice, D(2))).Success);
        Assert.Empty(await Frozen());
        Assert.True((await _svc.CanStartAsync(_alice, D(2))).Allowed);
    }

    [Fact]
    public async Task WelcomeBack_OnceOnTheReturnDate()
    {
        await _svc.StartAsync(_alice, D(5), D(2));
        Assert.False(await _svc.TakeWelcomeBackAsync(_alice, D(4)));
        Assert.True(await _svc.TakeWelcomeBackAsync(_alice, D(5)));
        Assert.False(await _svc.TakeWelcomeBackAsync(_alice, D(5)));
    }

    [Fact]
    public async Task WelcomeBack_TheDayAfterEndingEarly()
    {
        await _svc.StartAsync(_alice, D(9), D(2));
        await _svc.EndEarlyAsync(_alice, D(3));
        Assert.True(await _svc.TakeWelcomeBackAsync(_alice, D(4)));
    }

    [Fact]
    public async Task EndEarly_NotOnVacation_IsRefused() =>
        Assert.False((await _svc.EndEarlyAsync(_alice, D(2))).Success);

    [Fact]
    public async Task State_ShowsTheVacationInProgress()
    {
        await _svc.StartAsync(_alice, D(5), D(2));
        var state = await _svc.GetStateAsync(_alice, D(3));
        Assert.Equal(new VacationPeriod(D(2), D(5)), state.Current);
        Assert.Equal(VacationRefusal.AlreadyOnVacation, state.Check.Refusal);
    }
}
