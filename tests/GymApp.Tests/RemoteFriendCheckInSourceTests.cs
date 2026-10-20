using System.Net;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using GymApp.Social;

namespace GymApp.Tests;

public sealed class RemoteFriendCheckInSourceTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private readonly TestDb _db = new();
    private readonly FakeSocialServer _server = new();
    private readonly SessionService _session = new();
    private readonly RemoteFriendCheckInSource _source;
    private readonly int _me;
    private readonly int _homeCity;
    private readonly int _worldClass;

    public RemoteFriendCheckInSourceTests()
    {
        _me = _db.AddUser("alice");
        using (var db = _db.CreateDbContext())
        {
            _session.SignIn(db.Users.Single(u => u.Id == _me));
            var city = new City { Name = "Timișoara", OsmType = OsmElementType.Relation, OsmId = 7, CenterLatitude = 45.75, CenterLongitude = 21.22 };
            var gym = new Gym { Name = "World Class Iulius", City = city, OsmType = OsmElementType.Way, OsmId = 101, Latitude = 45.766, Longitude = 21.227 };
            var closed = new Gym { Name = "Closed gym", City = city, OsmType = OsmElementType.Node, OsmId = 303, Latitude = 45.7, Longitude = 21.2, IsActive = false };
            db.Gyms.AddRange(gym, closed);
            db.SaveChanges();
            _homeCity = city.Id;
            _worldClass = gym.Id;
        }
        _source = new RemoteFriendCheckInSource(_server.Api, new SocialCacheStore(_db), _session, _db);
    }

    public void Dispose() => _db.Dispose();

    private static FriendCheckInResponse InTimisoara(int friend, string name, OsmType? type, long? id, double? lat, double? lon, int daysAgo = 0) =>
        new(friend, $"friend{friend}", Today.AddDays(-daysAgo), type, id, name, OsmType.Relation, 7, "Timișoara", lat, lon);

    private static readonly FriendCheckInResponse KnownGym = InTimisoara(901, "World Class", OsmType.Way, 101, 45.7661, 21.2271);
    private static readonly FriendCheckInResponse UnknownOsmGym = InTimisoara(902, "Stay Fit", OsmType.Node, 202, 45.74, 21.21);
    private static readonly FriendCheckInResponse CustomGym = InTimisoara(903, "Garage gym", null, null, null, null);
    private static readonly FriendCheckInResponse InCluj =
        new(904, "friend904", Today, OsmType.Way, 555, "Gym Cluj", OsmType.Relation, 99, "Cluj-Napoca", 46.77, 23.6);

    private void Serve(params FriendCheckInResponse[] rows) => _server.On("GET v1/friends/checkins", HttpStatusCode.OK, rows.ToList());

    [Fact] public async Task KnownCityAndGym_MapToTheLocalIds()
    {
        var c = Assert.Single(await _source.MapAsync([KnownGym]));
        Assert.Equal((901, "friend901", _worldClass, _homeCity, false), (c.FriendUserId, c.Username, c.GymId, c.CityId, c.IsCustomGym));
        Assert.Equal(("World Class Iulius", 45.766, 21.227), (c.GymName, c.Latitude, c.Longitude));   // as this phone knows it
        Assert.Null(c.DistanceKm);
    }

    [Fact] public async Task UnknownCity_GoesUnderOtherCities_ByTheServersName()
    {
        var c = Assert.Single(await _source.MapAsync([InCluj]));
        Assert.Null(c.CityId);
        Assert.True(c.GymId < 0);

        Serve(InCluj);
        var map = await new FriendMapService(_source).GetAsync(_me, _homeCity, null, null, MapRange.Month, MapMode.Latest, FriendSort.ByGym, Today);
        Assert.Empty(map.Layout.Pins);
        var group = Assert.Single(map.Groups);
        Assert.Equal((FriendGroupKind.City, "Cluj-Napoca", false), (group.Kind, group.Title, group.OnMap));
    }

    [Fact] public async Task UnknownOsmGymInTheHomeCity_StillGetsAPin_FromTheServersPosition()
    {
        Serve(UnknownOsmGym);
        var map = await new FriendMapService(_source).GetAsync(_me, _homeCity, null, (45.75, 21.22), MapRange.Month, MapMode.Latest, FriendSort.Recent, Today);

        var pin = Assert.Single(map.Layout.Pins);
        Assert.True(pin.GymId < 0);
        Assert.Equal(("Stay Fit", 45.74, 21.21), (pin.GymName, pin.Latitude, pin.Longitude));
        Assert.NotNull(pin.Newest.DistanceKm);   // from this phone's own origin
    }

    [Fact] public async Task FriendsCustomGym_GetsANegativeId_NoPosition_AndNoPin()
    {
        var c = Assert.Single(await _source.MapAsync([CustomGym with { Latitude = 45.7, Longitude = 21.2 }]));   // even if a position came
        Assert.True(c.GymId < 0);
        Assert.True(c.IsCustomGym);
        Assert.Equal((null, null), (c.Latitude, c.Longitude));
        Assert.Equal(_homeCity, c.CityId);

        Serve(CustomGym);
        var map = await new FriendMapService(_source).GetAsync(_me, _homeCity, null, null, MapRange.Month, MapMode.Latest, FriendSort.Recent, Today);
        Assert.Empty(map.Layout.Pins);
        Assert.Equal(FriendGroupKind.Elsewhere, Assert.Single(map.Groups).Kind);
    }

    [Fact] public async Task NegativeIds_AreUniquePerGym_AndSharedByFriendsAtTheSameOne()
    {
        var rows = await _source.MapAsync([
            UnknownOsmGym,
            UnknownOsmGym with { UserId = 905, Username = "friend905" },   // same unknown OSM gym
            CustomGym,
            CustomGym with { UserId = 906, Username = "friend906" },       // another friend's custom gym of the same name
            InTimisoara(907, "Closed gym", OsmType.Node, 303, 45.7, 21.2)   // inactive locally: unknown
        ]);

        Assert.All(rows, r => Assert.True(r.GymId < 0));
        Assert.Equal(rows[0].GymId, rows[1].GymId);
        Assert.NotEqual(rows[2].GymId, rows[3].GymId);
        Assert.Equal(4, rows.Select(r => r.GymId).Distinct().Count());
    }

    [Fact] public async Task GymPickerCounts_OnlyGymsThatMatched()
    {
        var rows = await _source.MapAsync([KnownGym, KnownGym with { UserId = 905 }, UnknownOsmGym, CustomGym]);

        var counts = FriendMapRules.FriendsAtGym(rows);
        Assert.Equal(new Dictionary<int, int> { [_worldClass] = 2 }, counts);
    }

    [Fact] public async Task Offline_ReadsTheCache_MarkedStale_AndKeepsTheSinceFilter()
    {
        Serve(KnownGym, InTimisoara(905, "World Class", OsmType.Way, 101, null, null, daysAgo: 10));
        Assert.Equal(2, (await _source.GetFriendGymCheckInsAsync(_me, Today.AddDays(-29))).Count);

        _server.Offline = true;
        var stale = await _source.GetFriendGymCheckInsAsync(_me, Today.AddDays(-7));

        Assert.Equal([901], stale.Select(c => c.FriendUserId));
        Assert.True(((ICachedResult)stale).IsStale);
        var map = await new FriendMapService(_source).GetAsync(_me, _homeCity, null, null, MapRange.Month, MapMode.Latest, FriendSort.Recent, Today);
        Assert.True(map.IsStale);
        Assert.NotNull(map.FetchedAt);
    }

    [Fact] public async Task Offline_WithNothingCached_IsEmptyAndStale()
    {
        _server.Offline = true;
        var rows = await _source.GetFriendGymCheckInsAsync(_me, Today.AddDays(-29));
        Assert.Empty(rows);
        Assert.Equal((true, null), (((ICachedResult)rows).IsStale, ((ICachedResult)rows).FetchedAt));
    }

    [Fact] public async Task SendsSince_AndActsOnlyAsTheSignedInUser()
    {
        string? query = null;
        _server.On("GET v1/friends/checkins", req => { query = req.RequestUri!.Query; return FakeSocialServer.Respond(HttpStatusCode.OK, new List<FriendCheckInResponse>()); });

        await _source.GetFriendGymCheckInsAsync(_me, new DateOnly(2026, 9, 15));
        Assert.Equal("?since=2026-09-15", query);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _source.GetFriendGymCheckInsAsync(_me + 1, Today));
    }
}
