using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class HomeGymTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private readonly TestDb _db = new();
    private readonly ProfileService _profiles;
    private readonly GymService _gyms;
    private readonly WorkoutService _workouts;
    private readonly int _me, _other, _city;

    public HomeGymTests()
    {
        _profiles = new ProfileService(_db);
        _gyms = new GymService(_db);
        _workouts = new WorkoutService(_db, new StreakService(_db), new PointsService(_db));
        _me = _db.AddUser("me");
        _other = _db.AddUser("other");
        using var db = _db.CreateDbContext();
        var c = new City { OsmType = OsmElementType.Relation, OsmId = 1, Name = "Timișoara", DisplayName = "Timișoara" };
        db.Cities.Add(c);
        db.SaveChanges();
        _city = c.Id;
    }

    public void Dispose() => _db.Dispose();

    private int AddGym(string name, int? owner = null, bool active = true)
    {
        using var db = _db.CreateDbContext();
        var g = new Gym
        {
            CityId = _city, Name = name, OwnerUserId = owner, IsActive = active,
            OsmType = owner is null ? OsmElementType.Node : null, OsmId = owner is null ? Random.Shared.NextInt64(1, long.MaxValue) : null
        };
        db.Gyms.Add(g);
        db.SaveChanges();
        return g.Id;
    }

    private UserProfile? Profile()
    {
        using var db = _db.CreateDbContext();
        return db.UserProfiles.AsNoTracking().FirstOrDefault(p => p.UserId == _me);
    }

    private int? TodaysGym()
    {
        using var db = _db.CreateDbContext();
        return db.WorkoutLogs.Single(w => w.UserId == _me && w.Date == Today).GymId;
    }

    [Fact] public async Task AcceptsABuiltInGym_AndYourOwnCustomGym()
    {
        var osm = AddGym("World Class");
        var mine = AddGym("My garage", owner: _me);

        Assert.True((await _profiles.SetHomeGymAsync(_me, osm)).Success);
        Assert.Equal(osm, Profile()!.HomeGymId);
        Assert.True((await _profiles.SetHomeGymAsync(_me, mine)).Success);
        Assert.Equal(mine, Profile()!.HomeGymId);
        Assert.True((await _profiles.SetHomeGymAsync(_me, null)).Success);
        Assert.Null(Profile()!.HomeGymId);
    }

    [Fact] public async Task RejectsSomeoneElsesCustomGym_AndAnInactiveGym()
    {
        var theirs = AddGym("Their garage", owner: _other);
        var closed = AddGym("Closed", active: false);

        var r1 = await _profiles.SetHomeGymAsync(_me, theirs);
        var r2 = await _profiles.SetHomeGymAsync(_me, closed);
        Assert.False(r1.Success);
        Assert.False(r2.Success);
        Assert.Equal(FormField.Gym, r1.Field);
        Assert.Null(Profile()?.HomeGymId);
    }

    [Fact] public async Task DeletingTheCustomGymThatIsYourGym_OnlyClearsIt()
    {
        var mine = AddGym("My garage", owner: _me);
        await _profiles.SetHomeGymAsync(_me, mine);

        // Never logged there, so it's really deleted (not just deactivated)
        Assert.True((await _gyms.DeleteCustomGymAsync(_me, mine)).Success);
        var p = Profile()!;
        Assert.Null(p.HomeGymId);
        Assert.Equal(_me, p.UserId);   // the profile itself stays
    }

    [Fact] public async Task AskForGymOff_ACheckInGetsYourGym()
    {
        _db.ReadyToCheckIn(_me, Today);
        var osm = AddGym("World Class");
        await _profiles.SetHomeGymAsync(_me, osm);
        await _profiles.SetAskForGymAfterCheckInAsync(_me, false);

        Assert.True((await _workouts.LogTodayAsync(_me, today: Today)).Success);
        Assert.Equal(osm, TodaysGym());
    }

    [Fact] public async Task AskForGymOff_WithoutYourGym_NoGymIsLogged()
    {
        _db.ReadyToCheckIn(_me, Today);
        await _profiles.SetAskForGymAfterCheckInAsync(_me, false);

        Assert.True((await _workouts.LogTodayAsync(_me, today: Today)).Success);
        Assert.Null(TodaysGym());
    }

    [Fact] public async Task AskForGymOn_NothingIsLoggedByItself_ButYourGymIsSuggested()
    {
        _db.ReadyToCheckIn(_me, Today);
        var osm = AddGym("World Class");
        await _profiles.SetHomeGymAsync(_me, osm);

        Assert.True((await _workouts.LogTodayAsync(_me, today: Today)).Success);
        Assert.Null(TodaysGym());
        var info = await _gyms.GetTodayAsync(_me, Today);
        Assert.Equal(osm, info.Suggested?.Id);
        Assert.Null(info.ShareGymCheckIns);   // not asked yet
    }

    [Fact] public async Task ChangingTodaysGym_NeverChangesYourGym()
    {
        _db.ReadyToCheckIn(_me, Today);
        var home = AddGym("World Class");
        var elsewhere = AddGym("Stay Fit");
        await _profiles.SetHomeGymAsync(_me, home);
        await _workouts.LogTodayAsync(_me, today: Today);

        Assert.True((await _gyms.SetGymForTodayAsync(_me, elsewhere, Today)).Success);
        Assert.Equal(elsewhere, TodaysGym());
        Assert.Equal(home, Profile()!.HomeGymId);
    }
}
