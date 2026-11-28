using GymApp.App;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

/// <summary>Noticing a trip away from the home city (19c). Alice lives in Timișoara, trains every day, logged Mon and
/// Tue; "now" is Wednesday morning, so she has a 2-day streak to keep.</summary>
public sealed class AwayCheckTests : IDisposable
{
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static DateOnly D(int n) => Mon.AddDays(n);
    private static DateTime At(int day, int hour) => D(day).ToDateTime(new TimeOnly(hour, 0));

    // Timișoara's box, roughly
    private const double S = 45.70, W = 21.15, N = 45.81, E = 21.32;
    private static readonly LocationFix Home = new(45.75, 21.23, 20);
    private static readonly LocationFix Rome = new(41.90, 12.50, 20);

    private readonly TestDb _db = new();
    private readonly VacationService _svc;
    private readonly int _alice;

    public AwayCheckTests()
    {
        _svc = new VacationService(_db, new StreakService(_db), new PointsService(_db));
        _alice = AddUser("alice", withCity: true);
    }

    public void Dispose() => _db.Dispose();

    private int AddUser(string name, bool withCity, params int[] logged)
    {
        var id = _db.AddUser(name);
        using var db = _db.CreateDbContext();
        var city = withCity ? new City { OsmType = OsmElementType.Relation, OsmId = 1000 + id, Name = "Timișoara",
            DisplayName = "Timișoara, România", CenterLatitude = 45.75, CenterLongitude = 21.23, South = S, West = W, North = N, East = E } : null;
        db.UserProfiles.Add(new UserProfile { UserId = id, HomeCity = city });
        db.UserSchedules.Add(new UserSchedule { UserId = id, EffectiveFrom = Mon, WeekPattern = "WWWWWWW" });
        foreach (var d in logged.Length == 0 ? [0, 1] : logged) db.WorkoutLogs.Add(new WorkoutLog { UserId = id, Date = D(d) });
        db.SaveChanges();
        return id;
    }

    private UserProfile Profile(int id)
    {
        using var db = _db.CreateDbContext();
        return db.UserProfiles.AsNoTracking().Single(p => p.UserId == id);
    }

    // The rule

    [Fact]
    public void Due_AtMostEverySixHours()
    {
        Assert.True(VacationRules.IsAwayCheckDue(At(2, 8), null));
        Assert.False(VacationRules.IsAwayCheckDue(At(2, 13), At(2, 8)));
        Assert.True(VacationRules.IsAwayCheckDue(At(2, 14), At(2, 8)));
        Assert.True(VacationRules.IsAwayCheckDue(At(2, 7), At(2, 8)));   // the clock went back (TestBench): look again
    }

    // When Home looks

    [Fact]
    public async Task Due_WithAHomeCityAndAStreak()
    {
        var target = await _svc.AwayCheckDueAsync(_alice, At(2, 8));
        Assert.Equal("Timișoara", target!.CityName);
        Assert.Equal(N, target.North);
    }

    [Fact]
    public async Task NotDue_WithoutAHomeCity() =>
        Assert.Null(await _svc.AwayCheckDueAsync(AddUser("bob", withCity: false), At(2, 8)));

    [Fact]
    public async Task NotDue_WithNoStreak() =>
        Assert.Null(await _svc.AwayCheckDueAsync(AddUser("bob", withCity: true, 0), At(5, 8)));   // broken long ago

    [Fact]
    public async Task NotDue_WithinSixHoursOfTheLastLook()
    {
        var home = (await _svc.AwayCheckDueAsync(_alice, At(2, 8)))!;
        await _svc.RecordAwayFixAsync(_alice, home, Home, At(2, 8));
        Assert.Null(await _svc.AwayCheckDueAsync(_alice, At(2, 13)));
        Assert.NotNull(await _svc.AwayCheckDueAsync(_alice, At(2, 14)));
    }

    [Fact]
    public async Task NotDue_OnVacation_OrDuringTheCooldown()
    {
        await _svc.StartAsync(_alice, D(4), D(2));
        Assert.Null(await _svc.AwayCheckDueAsync(_alice, At(3, 8)));    // on vacation
        Assert.Null(await _svc.AwayCheckDueAsync(_alice, At(10, 8)));   // back, but within 60 days
    }

    [Fact]
    public async Task Due_WithAMissedDayToSettle_SoThePromptCanSaySo()
    {
        var bob = AddUser("bob", withCity: true, 0);   // Tue missed: on Wed he must choose
        Assert.NotNull(await _svc.AwayCheckDueAsync(bob, At(2, 8)));
    }

    // What a look decides

    [Fact]
    public async Task AFixAway_Asks_AndTheLookIsRecorded()
    {
        var home = (await _svc.AwayCheckDueAsync(_alice, At(2, 8)))!;
        Assert.True(await _svc.RecordAwayFixAsync(_alice, home, Rome, At(2, 8)));
        Assert.Equal(At(2, 8), Profile(_alice).LastAwayCheckAt);
    }

    [Fact]
    public async Task AFixAtHome_DoesntAsk_AndTheLookIsRecorded()
    {
        var home = (await _svc.AwayCheckDueAsync(_alice, At(2, 8)))!;
        Assert.False(await _svc.RecordAwayFixAsync(_alice, home, Home, At(2, 8)));
        Assert.Equal(At(2, 8), Profile(_alice).LastAwayCheckAt);
    }

    [Fact]
    public async Task NoFix_DoesntAsk_AndIsntALook()   // GPS didn't answer: the 6-hour wait mustn't start
    {
        var home = (await _svc.AwayCheckDueAsync(_alice, At(2, 8)))!;
        Assert.False(await _svc.RecordAwayFixAsync(_alice, home, null, At(2, 8)));
        Assert.Null(Profile(_alice).LastAwayCheckAt);
        Assert.NotNull(await _svc.AwayCheckDueAsync(_alice, At(2, 9)));
    }

    [Fact]
    public async Task NotOnVacation_AsksOncePerTrip()
    {
        var home = (await _svc.AwayCheckDueAsync(_alice, At(2, 8)))!;
        await _svc.DismissAwayPromptAsync(_alice, At(2, 8));
        Assert.False(await _svc.RecordAwayFixAsync(_alice, home, Rome, At(2, 15)));   // same trip

        Assert.False(await _svc.RecordAwayFixAsync(_alice, home, Home, At(3, 8)));    // back home: the trip is over
        Assert.Null(Profile(_alice).VacationPromptDismissedAt);
        Assert.True(await _svc.RecordAwayFixAsync(_alice, home, Rome, At(4, 8)));     // the next trip asks again
    }

    // The watcher on Home

    private sealed class FakeLocation(LocationFix? fix, bool permission = true, bool precise = true) : GymApp.App.ILocationProvider
    {
        public LocationFix? Fix { get; set; } = fix;
        public int Reads { get; private set; }
        public bool IsSupported => true;
        public bool HasPermission => permission;
        public bool IsPrecise => precise;
        public bool NeedsNoteBeforePrompt => false;
        public Task<bool> RequestPermissionAsync() => throw new InvalidOperationException("The away check never asks.");
        public Task<LocationFix?> GetFixAsync(TimeSpan timeout) { Reads++; return Task.FromResult(Fix); }
        public void OpenSettings() { }
    }

    // The watcher reads AppClock, so these use a user whose streak is alive today
    private int AliceToday()
    {
        var today = AppClock.Today;
        var id = _db.AddUser("today");
        using var db = _db.CreateDbContext();
        db.UserProfiles.Add(new UserProfile { UserId = id, HomeCity = new City { OsmType = OsmElementType.Relation, OsmId = 5000,
            Name = "Timișoara", DisplayName = "Timișoara", CenterLatitude = 45.75, CenterLongitude = 21.23, South = S, West = W, North = N, East = E } });
        db.UserSchedules.Add(new UserSchedule { UserId = id, EffectiveFrom = today.AddDays(-10), WeekPattern = "WWWWWWW" });
        db.WorkoutLogs.AddRange(new WorkoutLog { UserId = id, Date = today.AddDays(-2) }, new WorkoutLog { UserId = id, Date = today.AddDays(-1) });
        db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task Watcher_Away_ShowsThePrompt_UntilAnswered()
    {
        var user = AliceToday();
        var loc = new FakeLocation(Rome);
        var watcher = new AwayWatcher(_svc, loc);

        Assert.Equal("Timișoara", await watcher.CheckAsync(user));
        Assert.Equal("Timișoara", await watcher.CheckAsync(user));   // still up, without reading again
        Assert.Equal(1, loc.Reads);

        await watcher.DismissAsync(user);
        Assert.Null(watcher.Pending(user));
        Assert.NotNull(Profile(user).VacationPromptDismissedAt);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Watcher_WithoutPreciseLocation_NeverReads(bool permission, bool precise)
    {
        var loc = new FakeLocation(Rome, permission, precise);
        Assert.Null(await new AwayWatcher(_svc, loc).CheckAsync(AliceToday()));
        Assert.Equal(0, loc.Reads);
    }

    [Fact]
    public async Task Watcher_AtHome_NoPrompt()
    {
        Assert.Null(await new AwayWatcher(_svc, new FakeLocation(Home)).CheckAsync(AliceToday()));
    }

    [Fact]
    public async Task Watcher_NoFix_TriesAgainAfterFifteenMinutes_NotSixHours()
    {
        var user = AliceToday();
        var now = AppClock.Now;
        var loc = new FakeLocation(null);
        var watcher = new AwayWatcher(_svc, loc) { Now = () => now };

        Assert.Null(await watcher.CheckAsync(user));          // GPS didn't answer
        Assert.Equal(1, loc.Reads);
        now += TimeSpan.FromMinutes(5);
        Assert.Null(await watcher.CheckAsync(user));          // Home again soon: no new read
        Assert.Equal(1, loc.Reads);

        loc.Fix = Rome;
        now += AwayWatcher.NoFixRetry;
        Assert.Equal("Timișoara", await watcher.CheckAsync(user));   // tried again, well before 6 hours
        Assert.Equal(2, loc.Reads);
    }
}
