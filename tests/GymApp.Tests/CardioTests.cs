using GymApp.App.ViewModels;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

/// <summary>The cardio timer's clock (0.18.2): what counts, and what leaving the gym or a stop does to it.</summary>
public class CardioRulesTests
{
    private static readonly DateTime T0 = new(2026, 11, 9, 18, 0, 0);

    [Fact] public void Seconds_AddTheRunningStretch_ToWhatWasCounted()
    {
        Assert.Equal(90 + 600, CardioRules.Seconds(new CardioClock(90, T0, null, false), T0.AddMinutes(10)));
        Assert.Equal(90, CardioRules.Seconds(new CardioClock(90, null, null, false), T0.AddMinutes(10)));   // paused
    }

    [Fact] public void LeavingTheGym_CountsOnlyUpToTheLastCheckThere()
    {
        var clock = new CardioClock(60, T0, T0.AddMinutes(12), true);
        Assert.Equal(60 + 12 * 60, CardioRules.SecondsWhenLeft(clock));
        // Resumed away from the gym, never found there since: nothing more
        Assert.Equal(60, CardioRules.SecondsWhenLeft(new CardioClock(60, T0, T0.AddMinutes(-5), true)));
    }

    [Fact] public void AStop_CountsAChecked_TimerOnlyUpToItsLastCheck()
    {
        var stop = T0.AddHours(4);
        Assert.Equal(20 * 60, CardioRules.SecondsAtStop(new CardioClock(0, T0, T0.AddMinutes(20), true), stop));
        Assert.Equal(4 * 3600, CardioRules.SecondsAtStop(new CardioClock(0, T0, null, false), stop));
        Assert.Equal(300, CardioRules.SecondsAtStop(new CardioClock(300, null, T0, true), stop));
    }

    [Theory]
    [InlineData(0, 10)] [InlineData(9, 10)] [InlineData(10, 20)] [InlineData(29, 30)] [InlineData(30, null)] [InlineData(45, null)]
    public void NextPointAt(int minutes, int? expected) => Assert.Equal(expected, CardioRules.NextPointAt(minutes));

    [Fact] public void Progress_FillsTowardTheNextPoint_ThenStaysFull()
    {
        Assert.Equal(0, CardioRules.ProgressToNextPoint(0));
        Assert.Equal(0.5, CardioRules.ProgressToNextPoint(15 * 60), 3);
        Assert.Equal(1, CardioRules.ProgressToNextPoint(31 * 60));
    }

    [Fact] public void Minutes_AreWhole_AndCapped()
    {
        Assert.Equal(14, CardioRules.Minutes(14 * 60 + 59));
        Assert.Equal(PointsCalculator.MaxCardioMinutes, CardioRules.Minutes(99_999));
    }

    [Theory]
    [InlineData(0, "0:00")] [InlineData(847, "14:07")] [InlineData(3725, "1:02:05")]
    public void TheCardsTime(int seconds, string text) => Assert.Equal(text, CardioCardViewModel.FormatTime(seconds));

    [Theory]
    [InlineData(0, "End cardio")] [InlineData(4, "Save 4 min")] [InlineData(14, "Save 14 min · +1")] [InlineData(40, "Save 40 min · +3")]
    public void TheSaveLabel(int minutes, string text) => Assert.Equal(text, CardioCardViewModel.SaveLabel(minutes));
}

/// <summary>The cardio timer over the database: start after a logged workout, the gym checks, pause, finish and the
/// stop at 4 hours or midnight. The minutes land on the day's log and earn cardio points.</summary>
public class CardioServiceTests : IDisposable
{
    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private static readonly DateOnly Mon = new(2026, 11, 9);
    private readonly TestDb _db = new();
    private readonly Clock _clock = new(new DateTime(2026, 11, 9, 18, 0, 0));
    private readonly WorkoutService _workouts;
    private readonly CardioService _svc;
    private readonly int _alice, _legs;

    private static readonly LocationFix AtGym = new(45.7501, 21.2300, 20);
    private static readonly LocationFix Away = new(45.7600, 21.2300, 20);   // ~1.1 km

    public CardioServiceTests()
    {
        _alice = _db.AddUser("alice");
        _legs = _db.AddSplit(_alice, ("Legs", DayOfWeek.Monday))[0];
        _db.AddDayExercises(_alice, _legs, 2);
        _workouts = new WorkoutService(_db, new StreakService(_db), new PointsService(_db));
        _svc = new CardioService(_db, _workouts, _clock);
    }

    public void Dispose() => _db.Dispose();

    private int AddGym()
    {
        using var db = _db.CreateDbContext();
        var city = new City { OsmType = OsmElementType.Relation, OsmId = 7, Name = "Timișoara", DisplayName = "Timișoara" };
        db.Cities.Add(city);
        db.SaveChanges();
        var gym = new Gym { CityId = city.Id, Name = "Iron Gym", Latitude = 45.75, Longitude = 21.23, OsmType = OsmElementType.Node, OsmId = 42 };
        db.Gyms.Add(gym);
        db.SaveChanges();
        return gym.Id;
    }

    /// <summary>Today's workout, as a finished live workout leaves it.</summary>
    private Task Log(int? verifiedGymId = null)
    {
        using var db = _db.CreateDbContext();
        db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = DateOnly.FromDateTime(_clock.Now), SplitDayId = _legs,
            GymId = verifiedGymId, Verified = verifiedGymId is not null, FilledExercises = 2, TotalExercises = 2 });
        db.SaveChanges();
        return Task.CompletedTask;
    }

    private async Task<CardioInfo> Start()
    {
        var (info, error) = await _svc.StartAsync(_alice);
        Assert.True(info is not null, error);
        return info!;
    }

    private void Wait(int minutes) => _clock.Now = _clock.Now.AddMinutes(minutes);

    private int? LoggedMinutes()
    {
        using var db = _db.CreateDbContext();
        return db.WorkoutLogs.AsNoTracking().Single(w => w.UserId == _alice && w.Date == Mon).CardioMinutes;
    }

    private int CardioPoints()
    {
        using var db = _db.CreateDbContext();
        return db.PointsEntries.Where(p => p.UserId == _alice && p.Reason == PointsReason.Cardio && p.Date == Mon).Sum(p => p.Amount);
    }

    [Fact] public async Task Start_NeedsTodaysLog()
    {
        var (info, error) = await _svc.StartAsync(_alice);
        Assert.Null(info);
        Assert.Equal("Log the workout first.", error);
    }

    [Fact] public async Task AnUnverifiedWorkout_RunsUnchecked_AndSavesItsMinutes()
    {
        await Log();
        var info = await Start();
        Assert.False(info.IsChecked);
        Assert.True(info.IsRunning);

        Wait(23);
        Assert.Equal(23 * 60, (await _svc.GetOpenAsync(_alice))!.Seconds(_clock.Now));
        var r = await _svc.FinishAsync(_alice);
        Assert.Equal((true, 23, 2), (r.Success, r.Minutes, r.Points));
        Assert.Equal(23, LoggedMinutes());
        Assert.Equal(2, CardioPoints());
        Assert.Null(await _svc.GetOpenAsync(_alice));
    }

    [Fact] public async Task Start_Twice_KeepsOneTimer_AndTheDatabaseEnforcesIt()
    {
        await Log();
        var first = await Start();
        Assert.Equal(first.Id, (await Start()).Id);
        using var db = _db.CreateDbContext();
        db.CardioSessions.Add(new CardioSession { UserId = _alice, Date = Mon, StartedAt = _clock.Now });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact] public async Task AVerifiedWorkout_IsCheckedAgainstItsGym()
    {
        var gym = AddGym();
        await Log(gym);
        var info = await Start();
        Assert.True(info.IsChecked);
        Assert.Equal("Iron Gym", info.GymName);
    }

    [Fact] public async Task LeavingTheGym_Pauses_AtTheLastCheckThere()
    {
        await Log(AddGym());
        await Start();
        Wait(8);
        Assert.True((await _svc.CheckAsync(_alice, AtGym))!.IsRunning);
        Wait(1);
        var away = (await _svc.CheckAsync(_alice, Away))!;
        Assert.Equal(CardioPause.LeftGym, away.Pause);
        Assert.Equal(8 * 60, away.Seconds(_clock.Now));   // the last minute wasn't seen at the gym

        Wait(30);   // still away: nothing more counts
        Assert.Equal(8 * 60, (await _svc.GetOpenAsync(_alice))!.Seconds(_clock.Now));
    }

    [Fact] public async Task BackAtTheGym_ItGoesOnByItself()
    {
        await Log(AddGym());
        await Start();
        Wait(5);
        await _svc.CheckAsync(_alice, AtGym);
        Wait(1);
        await _svc.CheckAsync(_alice, Away);
        Wait(10);
        var back = (await _svc.CheckAsync(_alice, AtGym))!;
        Assert.True(back.IsRunning);
        Wait(6);
        Assert.Equal(11 * 60, back.Seconds(_clock.Now));   // 5 before leaving, 6 after coming back
    }

    [Fact] public async Task ALockedPhone_StillAtTheGym_CountsTheWholeTime()
    {
        await Log(AddGym());
        await Start();
        Wait(25);   // no checks while the phone was locked
        var info = (await _svc.CheckAsync(_alice, AtGym))!;
        Assert.Equal(25 * 60, info.Seconds(_clock.Now));
    }

    [Fact] public async Task AVagueFix_ChangesNothing()
    {
        await Log(AddGym());
        await Start();
        Wait(5);
        var info = (await _svc.CheckAsync(_alice, new LocationFix(45.80, 21.23, 900)))!;
        Assert.True(info.IsRunning);
        Assert.Equal(5 * 60, info.Seconds(_clock.Now));
    }

    [Fact] public async Task AnUncheckedTimer_IgnoresPositions()
    {
        await Log();
        await Start();
        Wait(5);
        Assert.True((await _svc.CheckAsync(_alice, Away))!.IsRunning);
    }

    [Fact] public async Task Pause_KeepsTheTime_ResumeGoesOn()
    {
        await Log();
        await Start();
        Wait(4);
        var paused = (await _svc.PauseAsync(_alice))!;
        Assert.Equal(CardioPause.User, paused.Pause);
        Wait(20);
        Assert.Equal(4 * 60, paused.Seconds(_clock.Now));
        await _svc.ResumeAsync(_alice);
        Wait(3);
        Assert.Equal(7 * 60, (await _svc.GetOpenAsync(_alice))!.Seconds(_clock.Now));
    }

    [Fact] public async Task UnderAMinute_SavesNothing()
    {
        await Log();
        await Start();
        _clock.Now = _clock.Now.AddSeconds(40);
        var r = await _svc.FinishAsync(_alice);
        Assert.Equal((true, 0), (r.Success, r.Minutes));
        Assert.Null(LoggedMinutes());
    }

    [Fact] public async Task AtMidnight_ItStops_AndSavesWhatItCounted()
    {
        _clock.Now = new DateTime(2026, 11, 9, 23, 40, 0);
        await Log();
        await Start();
        _clock.Now = new DateTime(2026, 11, 10, 7, 0, 0);
        Assert.Null(await _svc.GetOpenAsync(_alice));
        Assert.Equal(20, LoggedMinutes());
    }

    [Fact] public async Task AStoppedCheckedTimer_CountsUpToItsLastCheckAtTheGym()
    {
        await Log(AddGym());
        await Start();
        Wait(15);
        await _svc.CheckAsync(_alice, AtGym);
        _clock.Now = _clock.Now.AddHours(5);   // never opened again
        Assert.True(await _svc.StopIfDueAsync(_alice));
        Assert.Null(await _svc.GetOpenAsync(_alice));
        Assert.Equal(15, LoggedMinutes());
    }
}
