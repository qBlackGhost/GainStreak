using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

public class FriendMapServiceTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private readonly TestDb _db = new();
    private readonly FriendMapService _svc;
    private readonly WorkoutService _workouts;
    private readonly int _me, _ana, _bob, _city, _cluj, _gym, _clujGym;

    public FriendMapServiceTests()
    {
        _svc = new FriendMapService(new LocalSocialStore(_db));
        _workouts = new WorkoutService(_db, new StreakService(_db), new PointsService(_db));
        _me = _db.AddUser("me");
        _ana = _db.AddUser("ana");
        _bob = _db.AddUser("bob");
        _city = AddCity(1, "Timișoara");
        _cluj = AddCity(2, "Cluj-Napoca");
        _gym = AddGym("World Class", _city);
        _clujGym = AddGym("Cluj Gym", _cluj);
    }

    public void Dispose() => _db.Dispose();

    private int AddCity(long osmId, string name)
    {
        using var db = _db.CreateDbContext();
        var c = new City { OsmType = OsmElementType.Relation, OsmId = osmId, Name = name, DisplayName = name, CenterLatitude = 45.75, CenterLongitude = 21.22 };
        db.Cities.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    /// <summary>Custom gyms here still get a position, to prove the store never hands it out.</summary>
    private int AddGym(string name, int cityId, int? owner = null)
    {
        using var db = _db.CreateDbContext();
        var g = new Gym
        {
            CityId = cityId, Name = name, Latitude = 45.76, Longitude = 21.23, OwnerUserId = owner,
            OsmType = owner is null ? OsmElementType.Node : null, OsmId = owner is null ? Random.Shared.NextInt64(1, long.MaxValue) : null
        };
        db.Gyms.Add(g);
        db.SaveChanges();
        return g.Id;
    }

    private void Friends(int a, int b, FriendshipStatus status = FriendshipStatus.Accepted)
    {
        using var db = _db.CreateDbContext();
        db.Friendships.Add(new Friendship
            { UserAId = Math.Min(a, b), UserBId = Math.Max(a, b), RequestedById = a, Status = status });
        db.SaveChanges();
    }

    private void Share(int userId, bool? share)
    {
        using var db = _db.CreateDbContext();
        var p = db.UserProfiles.FirstOrDefault(x => x.UserId == userId);
        if (p is null) db.UserProfiles.Add(p = new UserProfile { UserId = userId });
        p.ShareGymCheckIns = share;
        db.SaveChanges();
    }

    private void Log(int userId, DateOnly date, int? gymId)
    {
        using var db = _db.CreateDbContext();
        db.WorkoutLogs.Add(new WorkoutLog { UserId = userId, Date = date, GymId = gymId });
        db.SaveChanges();
    }

    private Task<FriendMapResult> Map(MapRange range = MapRange.Month, (double, double)? origin = null) =>
        _svc.GetAsync(_me, _city, null, origin, range, MapMode.Latest, FriendSort.Recent, Today);

    [Fact] public async Task OnlyFriendsWhoShare_Appear()
    {
        Friends(_me, _ana);
        Friends(_me, _bob);
        Share(_ana, true);
        Share(_bob, false);
        Log(_ana, Today, _gym);
        Log(_bob, Today, _gym);

        var r = await Map();
        Assert.Equal(["ana"], r.Layout.LatestPerFriend.Select(c => c.Username));
        Assert.Equal("World Class", Assert.Single(r.Layout.Pins).GymName);
    }

    [Fact] public async Task NotAskedYet_SharesNothing()
    {
        Friends(_me, _ana);
        Share(_ana, null);
        Log(_ana, Today, _gym);
        Assert.Empty((await Map()).Layout.LatestPerFriend);
    }

    [Fact] public async Task APendingRequest_SharesNothing()
    {
        Friends(_me, _ana, FriendshipStatus.Pending);
        Share(_ana, true);
        Log(_ana, Today, _gym);
        Assert.Empty((await Map()).Layout.LatestPerFriend);
    }

    [Fact] public async Task ALogWithoutAGym_AndOneOlderThan30Days_DontAppear()
    {
        Friends(_me, _ana);
        Share(_ana, true);
        Log(_ana, Today, null);
        Log(_ana, Today.AddDays(-30), _gym);
        Assert.Empty((await Map()).Layout.LatestPerFriend);
    }

    [Fact] public async Task TurningSharingOff_HidesPastCheckIns()
    {
        Friends(_me, _ana);
        Share(_ana, true);
        Log(_ana, Today.AddDays(-3), _gym);
        Assert.Single((await Map()).Layout.LatestPerFriend);

        Assert.True((await new ProfileService(_db).SetShareGymCheckInsAsync(_ana, false)).Success);
        Assert.Empty((await Map()).Layout.LatestPerFriend);
    }

    [Fact] public async Task UndoingTodaysCheckIn_RemovesIt()
    {
        Friends(_me, _ana);
        Share(_ana, true);
        Log(_ana, Today, _gym);
        Assert.Single((await Map()).Layout.LatestPerFriend);

        await _workouts.UndoTodayAsync(_ana, Today);
        Assert.Empty((await Map()).Layout.LatestPerFriend);
    }

    [Fact] public async Task ACustomGym_ComesWithoutCoordinates_AndIsNeverAPin()
    {
        Friends(_me, _ana);
        Share(_ana, true);
        var custom = AddGym("Ana's garage", _city, owner: _ana);
        Log(_ana, Today, custom);

        var r = await Map(origin: (45.75, 21.22));
        var c = Assert.Single(r.Layout.LatestPerFriend);
        Assert.True(c.IsCustomGym);
        Assert.Null(c.Latitude);
        Assert.Null(c.Longitude);
        Assert.Null(c.DistanceKm);
        Assert.Empty(r.Layout.Pins);
        Assert.Equal(FriendGroupKind.Elsewhere, Assert.Single(r.Groups).Kind);
    }

    [Fact] public async Task WithAnOrigin_DistanceIsFilledIn_AndOtherCitiesAreElsewhere()
    {
        Friends(_me, _ana);
        Friends(_me, _bob);
        Share(_ana, true);
        Share(_bob, true);
        Log(_ana, Today, _gym);
        Log(_bob, Today.AddDays(-1), _clujGym);

        var r = await Map(origin: (45.75, 21.22));
        Assert.NotNull(r.Layout.LatestPerFriend.Single(c => c.Username == "ana").DistanceKm);
        Assert.Single(r.Layout.Pins);   // Cluj is another city: no pin
        Assert.Equal("Cluj-Napoca", r.Layout.LatestPerFriend.Single(c => c.Username == "bob").CityName);
        Assert.Equal([FriendGroupKind.Today, FriendGroupKind.Elsewhere], r.Groups.Select(g => g.Kind));
    }
}
