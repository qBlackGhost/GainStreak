using System.Net;
using System.Text;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

public class GymAddressLookupServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly int _alice;
    private readonly int _city;

    public GymAddressLookupServiceTests()
    {
        _alice = _db.AddUser("alice");
        using var db = _db.CreateDbContext();
        var c = new City { OsmType = OsmElementType.Relation, OsmId = 1, Name = "Timișoara", DisplayName = "Timișoara",
            CenterLatitude = 45.75, CenterLongitude = 21.22, South = 45.68, West = 21.11, North = 45.83, East = 21.33 };
        db.Cities.Add(c);
        db.SaveChanges();
        _city = c.Id;
    }

    public void Dispose() => _db.Dispose();

    private int AddGym(string name, string? address = null, bool active = true, int? owner = null, double? lat = 45.76)
    {
        using var db = _db.CreateDbContext();
        var g = new Gym
        {
            CityId = _city, Name = name, Address = address, IsActive = active, OwnerUserId = owner,
            OsmType = owner is null ? OsmElementType.Node : null, OsmId = owner is null ? Random.Shared.NextInt64(1, long.MaxValue) : null,
            Latitude = lat, Longitude = lat is null ? null : 21.22
        };
        db.Gyms.Add(g);
        db.SaveChanges();
        return g.Id;
    }

    private Gym Gym(int id)
    {
        using var db = _db.CreateDbContext();
        return db.Gyms.Single(g => g.Id == id);
    }

    private static string Reverse(string road, string suburb) =>
        $$$"""{"address":{"road":"{{{road}}}","suburb":"{{{suburb}}}"}}""";

    private GymAddressLookupService Svc(FakeHttpHandler h) =>
        new(_db, new GeocodingService(h, TimeSpan.Zero));

    [Fact] public async Task FillsOnlyOsmGymsWithoutAnAddress()
    {
        var missing = AddGym("World Class");
        var tagged = AddGym("Smartfit", address: "Calea Buziașului 11");
        var closed = AddGym("Closed", active: false);
        var custom = AddGym("Garage", owner: _alice, lat: null);
        var h = FakeHttpHandler.Json(Reverse("Piața Consiliul Europei", "Lipovei"));
        var svc = Svc(h);

        Assert.Equal(1, await svc.CountMissingAsync(_city));
        var r = await svc.FillMissingAsync(_city);

        Assert.Equal((1, 1, 0, (string?)null), (r.Looked, r.Found, r.Remaining, r.Error));
        Assert.Single(h.Requests);
        Assert.Equal("Piața Consiliul Europei, Lipovei", Gym(missing).LookedUpAddress);
        Assert.NotNull(Gym(missing).AddressLookedUpAt);
        Assert.Null(Gym(tagged).LookedUpAddress);
        Assert.Null(Gym(closed).AddressLookedUpAt);
        Assert.Null(Gym(custom).AddressLookedUpAt);
    }

    [Fact] public async Task EachGymIsLookedUpOnce()
    {
        AddGym("A");
        AddGym("B");
        var h = FakeHttpHandler.Json(Reverse("Strada X", "Fabric"));
        var svc = Svc(h);

        await svc.FillMissingAsync(_city);
        var again = await svc.FillMissingAsync(_city);

        Assert.Equal(2, h.Requests.Count);
        Assert.Equal(0, again.Looked);
        Assert.Equal(0, await svc.CountMissingAsync(_city));
    }

    [Fact] public async Task NothingFound_IsRemembered_AndNotRetried()
    {
        var id = AddGym("A");
        var h = FakeHttpHandler.Json(Fixtures.Read("nominatim_reverse_nothing.json"));
        var svc = Svc(h);

        var r = await svc.FillMissingAsync(_city);

        Assert.Equal((1, 0), (r.Looked, r.Found));
        Assert.Null(Gym(id).LookedUpAddress);
        Assert.NotNull(Gym(id).AddressLookedUpAt);
        await svc.FillMissingAsync(_city);
        Assert.Single(h.Requests);
    }

    [Fact] public async Task AFailure_StopsTheBatch_AndTheRestIsTriedNextTime()
    {
        var a = AddGym("A");
        var b = AddGym("B");
        var c = AddGym("C");
        var calls = 0;
        var h = new FakeHttpHandler((_, _) => ++calls == 2
            ? throw new HttpRequestException("offline")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(Reverse("Strada X", "Fabric"), Encoding.UTF8, "application/json") }));
        var svc = Svc(h);

        var r = await svc.FillMissingAsync(_city);

        Assert.Equal((1, 2), (r.Looked, r.Remaining));
        Assert.NotNull(r.Error);
        Assert.NotNull(Gym(a).AddressLookedUpAt);
        Assert.Null(Gym(b).AddressLookedUpAt);   // not marked, so it's retried
        Assert.Equal(2, await svc.CountMissingAsync(_city));

        var next = await svc.FillMissingAsync(_city);
        Assert.Equal((2, 0), (next.Looked, next.Remaining));
        Assert.NotNull(Gym(c).LookedUpAddress);
    }

    [Fact] public async Task ReportsProgress()
    {
        AddGym("A");
        AddGym("B");
        var seen = new List<AddressLookupProgress>();
        await Svc(FakeHttpHandler.Json(Reverse("Strada X", "Fabric")))
            .FillMissingAsync(_city, new SyncProgress<AddressLookupProgress>(seen.Add));
        Assert.Equal([new(1, 2), new(2, 2)], seen);
    }

    [Fact] public async Task ThePickerShowsTheLookedUpAddress_AsApproximate()
    {
        AddGym("World Class");
        AddGym("Smartfit", address: "Calea Buziașului 11");
        await Svc(FakeHttpHandler.Json(Reverse("Piața Consiliul Europei", "Lipovei"))).FillMissingAsync(_city);

        var rows = (await new GymService(_db).GetGymsForCityAsync(_alice, _city))!.Rows;

        var wc = rows.Single(r => r.Gym.Name == "World Class").Gym;
        Assert.Equal(("Piața Consiliul Europei, Lipovei", true), (wc.Address, wc.AddressIsApproximate));
        var sf = rows.Single(r => r.Gym.Name == "Smartfit").Gym;
        Assert.Equal(("Calea Buziașului 11", false), (sf.Address, sf.AddressIsApproximate));
    }

    /// <summary>Progress&lt;T&gt; posts to a sync context; this one reports straight away, in order.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
