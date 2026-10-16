using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class GymServiceTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    private readonly TestDb _db = new();
    private readonly GymService _svc;
    private readonly int _alice;
    private readonly int _bob;
    private readonly int _timisoara;
    private readonly int _arad;

    public GymServiceTests()
    {
        _svc = new GymService(_db);
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
        _timisoara = AddCity(1, "Timișoara", 45.7538355, 21.2257474);
        _arad = AddCity(2, "Arad", 46.1753793, 21.3196342);
    }

    public void Dispose() => _db.Dispose();

    private int AddCity(long osmId, string name, double lat, double lon)
    {
        using var db = _db.CreateDbContext();
        var c = new City { OsmType = OsmElementType.Relation, OsmId = osmId, Name = name, DisplayName = name, CenterLatitude = lat, CenterLongitude = lon };
        db.Cities.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    private int AddGym(string name, int? cityId = null, double? lat = 45.76, double? lon = 21.23, int? owner = null,
        bool active = true, long? osmId = null)
    {
        using var db = _db.CreateDbContext();
        var g = new Gym
        {
            CityId = cityId ?? _timisoara, Name = name, Latitude = owner is null ? lat : null, Longitude = owner is null ? lon : null,
            OwnerUserId = owner, IsActive = active,
            OsmType = owner is null ? OsmElementType.Node : null,
            OsmId = owner is null ? osmId ?? Random.Shared.NextInt64(1, long.MaxValue) : null
        };
        db.Gyms.Add(g);
        db.SaveChanges();
        return g.Id;
    }

    private void Log(int userId, DateOnly date, int? gymId = null)
    {
        using var db = _db.CreateDbContext();
        db.WorkoutLogs.Add(new WorkoutLog { UserId = userId, Date = date, GymId = gymId });
        db.SaveChanges();
    }

    private int? GymOn(int userId, DateOnly date)
    {
        using var db = _db.CreateDbContext();
        return db.WorkoutLogs.Single(w => w.UserId == userId && w.Date == date).GymId;
    }

    // ===== List =====

    [Fact] public async Task List_HasActiveOsmGyms_AndOnlyMyCustomGyms_InThatCity()
    {
        AddGym("Osm A");
        AddGym("Osm closed", active: false);
        AddGym("Alice custom", owner: _alice);
        AddGym("Alice custom closed", owner: _alice, active: false);
        AddGym("Bob custom", owner: _bob);
        AddGym("Arad gym", cityId: _arad);

        var r = (await _svc.GetGymsForCityAsync(_alice, _timisoara))!;

        Assert.Equal("Timișoara", r.City.Name);
        Assert.Equal(["Alice custom", "Osm A"], r.Rows.Select(x => x.Gym.Name));
        Assert.True(r.Rows[0].Gym.IsCustom);
        Assert.False(r.Rows[1].Gym.IsCustom);
    }

    [Fact] public async Task List_SearchIsCaseInsensitive()
    {
        AddGym("World Class Iulius");
        AddGym("Gym One");
        var r = (await _svc.GetGymsForCityAsync(_alice, _timisoara, "world"))!;
        Assert.Equal("World Class Iulius", Assert.Single(r.Rows).Gym.Name);
    }

    [Fact] public async Task List_ByDistance_FromTheCityCentre_CustomGymsLast()
    {
        AddGym("Far", lat: 45.80, lon: 21.30);
        AddGym("Near", lat: 45.754, lon: 21.226);
        AddGym("A custom", owner: _alice);

        var r = (await _svc.GetGymsForCityAsync(_alice, _timisoara, sort: GymSort.Distance))!;

        Assert.Equal(["Near", "Far", "A custom"], r.Rows.Select(x => x.Gym.Name));
        Assert.Null(r.Rows[2].DistanceKm);
    }

    [Fact] public async Task List_UnknownCity_IsNull() =>
        Assert.Null(await _svc.GetGymsForCityAsync(_alice, 999));

    // ===== Set today's gym =====

    [Fact] public async Task SetGymForToday_SetsChangesAndClears()
    {
        var a = AddGym("A");
        var b = AddGym("B");
        Log(_alice, Today);

        Assert.True((await _svc.SetGymForTodayAsync(_alice, a, Today)).Success);
        Assert.Equal(a, GymOn(_alice, Today));

        Assert.True((await _svc.SetGymForTodayAsync(_alice, b, Today)).Success);
        Assert.Equal(b, GymOn(_alice, Today));

        Assert.True((await _svc.SetGymForTodayAsync(_alice, null, Today)).Success);
        Assert.Null(GymOn(_alice, Today));
    }

    [Fact] public async Task SetGymForToday_WithoutATodayLog_Fails_AndYesterdayIsNotTouched()
    {
        var a = AddGym("A");
        var b = AddGym("B");
        Log(_alice, Yesterday, a);

        var r = await _svc.SetGymForTodayAsync(_alice, b, Today);

        Assert.False(r.Success);
        Assert.Equal(a, GymOn(_alice, Yesterday));
    }

    [Fact] public async Task YesterdaysLog_CannotBeChanged_OnceTheDayIsOver()
    {
        var a = AddGym("A");
        Log(_alice, Yesterday);
        Log(_alice, Today);

        await _svc.SetGymForTodayAsync(_alice, a, Today);

        Assert.Null(GymOn(_alice, Yesterday));
        Assert.Equal(a, GymOn(_alice, Today));
    }

    [Fact] public async Task SetGymForToday_InactiveGym_IsRejected()
    {
        var closed = AddGym("Closed", active: false);
        Log(_alice, Today);
        Assert.False((await _svc.SetGymForTodayAsync(_alice, closed, Today)).Success);
        Assert.Null(GymOn(_alice, Today));
    }

    [Fact] public async Task SetGymForToday_SomeoneElsesCustomGym_IsRejected_MyOwnIsFine()
    {
        var bobs = AddGym("Bob's", owner: _bob);
        var mine = AddGym("Mine", owner: _alice);
        Log(_alice, Today);

        Assert.False((await _svc.SetGymForTodayAsync(_alice, bobs, Today)).Success);
        Assert.True((await _svc.SetGymForTodayAsync(_alice, mine, Today)).Success);
    }

    [Fact] public async Task SetGymForToday_UnknownGym_IsRejected()
    {
        Log(_alice, Today);
        Assert.False((await _svc.SetGymForTodayAsync(_alice, 999, Today)).Success);
    }

    [Fact] public async Task SetGymForToday_OnlyChangesMyLog()
    {
        var a = AddGym("A");
        Log(_alice, Today);
        Log(_bob, Today);
        await _svc.SetGymForTodayAsync(_alice, a, Today);
        Assert.Null(GymOn(_bob, Today));
    }

    [Fact] public async Task PickingAGym_ChangesNoPointsAndNoStreak()
    {
        var points = new PointsService(_db);
        var workouts = new WorkoutService(_db, new StreakService(_db), points);
        await workouts.LogTodayAsync(_alice);
        int Entries() { using var db = _db.CreateDbContext(); return db.PointsEntries.Count(p => p.UserId == _alice); }
        (int, int, int) UserState() { using var db = _db.CreateDbContext(); var u = db.Users.Single(x => x.Id == _alice); return (u.Points, u.CurrentStreak, u.LongestStreak); }
        var before = (Entries(), UserState());

        var gym = AddGym("A");
        Assert.True((await _svc.SetGymForTodayAsync(_alice, gym)).Success);
        await _svc.SetGymForTodayAsync(_alice, null);

        Assert.Equal(before, (Entries(), UserState()));
    }

    // ===== Last used =====

    [Fact] public async Task LastUsed_IsTheGymOnTheMostRecentLogThatHasOne()
    {
        var a = AddGym("A");
        var b = AddGym("B");
        Log(_alice, Today.AddDays(-5), a);
        Log(_alice, Today.AddDays(-3), b);
        Log(_alice, Today.AddDays(-1));   // no gym: skipped

        Assert.Equal(new GymChoice(b, "B"), await _svc.GetLastUsedGymAsync(_alice));
    }

    [Fact] public async Task LastUsed_IsNotOffered_WhenThatGymWasDeactivated()
    {
        var a = AddGym("A");
        var b = AddGym("B");
        Log(_alice, Today.AddDays(-5), a);
        Log(_alice, Today.AddDays(-3), b);
        using (var db = _db.CreateDbContext())
        {
            db.Gyms.Single(g => g.Id == b).IsActive = false;
            db.SaveChanges();
        }

        Assert.Null(await _svc.GetLastUsedGymAsync(_alice));   // the most recent one, not an older fallback
    }

    [Fact] public async Task LastUsed_NoneYet_IsNull() => Assert.Null(await _svc.GetLastUsedGymAsync(_alice));

    // ===== Home =====

    [Fact] public async Task Today_BeforeCheckIn_IsNotLogged_AndAsksByDefault()
    {
        var t = await _svc.GetTodayAsync(_alice, Today);
        Assert.False(t.LoggedToday);
        Assert.Null(t.Gym);
        Assert.True(t.AskAfterCheckIn);
    }

    [Fact] public async Task Today_AfterCheckIn_OffersTheLastGymBeforeToday()
    {
        var a = AddGym("A");
        var b = AddGym("B");
        Log(_alice, Yesterday, a);
        Log(_alice, Today, b);

        var t = await _svc.GetTodayAsync(_alice, Today);

        Assert.True(t.LoggedToday);
        Assert.Equal(new GymChoice(b, "B"), t.Gym);
        Assert.Equal(new GymChoice(a, "A"), t.LastUsed);
    }

    [Fact] public async Task DontAskAgain_IsStoredOnTheProfile_AndCanBeTurnedBackOn()
    {
        var profiles = new ProfileService(_db);

        Assert.True((await profiles.SetAskForGymAfterCheckInAsync(_alice, false)).Success);
        Assert.False((await _svc.GetTodayAsync(_alice, Today)).AskAfterCheckIn);
        Assert.False((await profiles.GetAsync(_alice))!.Profile.AskForGymAfterCheckIn);

        Assert.True((await profiles.SetAskForGymAfterCheckInAsync(_alice, true)).Success);
        Assert.True((await _svc.GetTodayAsync(_alice, Today)).AskAfterCheckIn);
        Assert.True((await _svc.GetTodayAsync(_bob, Today)).AskAfterCheckIn);   // per user
    }
}
