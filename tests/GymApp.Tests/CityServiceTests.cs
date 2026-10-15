using GymApp.Core;
using GymApp.Services;

namespace GymApp.Tests;

public class CityServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly CityService _cities;
    private readonly ProfileService _profiles;
    private readonly int _alice;

    public CityServiceTests()
    {
        _cities = new CityService(_db);
        _profiles = new ProfileService(_db);
        _alice = _db.AddUser("alice");
    }

    public void Dispose() => _db.Dispose();

    private static CityCandidate Timisoara(string display = "Timișoara, Timiș, România") =>
        new(OsmElementType.Relation, 6927733, "Timișoara", display, 45.75, 21.22, 45.68, 21.11, 45.83, 21.33);

    [Fact] public async Task Upsert_AddsTheCity()
    {
        var city = await _cities.UpsertAsync(Timisoara());
        Assert.True(city.Id > 0);
        Assert.Null(city.GymsFetchedAt);

        var saved = (await _cities.GetAsync(city.Id))!;
        Assert.Equal("Timișoara, Timiș, România", saved.DisplayName);
        Assert.Equal(21.33, saved.East);
    }

    [Fact] public async Task Upsert_SameOsmObjectTwice_KeepsOneRow_UpdatesIt_AndKeepsTheGymCacheDate()
    {
        var first = await _cities.UpsertAsync(Timisoara());
        var fetched = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        using (var db = _db.CreateDbContext())
        {
            db.Cities.Single().GymsFetchedAt = fetched;
            db.SaveChanges();
        }

        var second = await _cities.UpsertAsync(Timisoara("Timișoara, Romania"));

        Assert.Equal(first.Id, second.Id);
        using var read = _db.CreateDbContext();
        var city = Assert.Single(read.Cities);
        Assert.Equal("Timișoara, Romania", city.DisplayName);
        Assert.Equal(fetched, city.GymsFetchedAt);
    }

    [Fact] public async Task SameOsmId_DifferentType_IsADifferentCity()
    {
        var a = await _cities.UpsertAsync(Timisoara());
        var b = await _cities.UpsertAsync(Timisoara() with { OsmType = OsmElementType.Node });
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact] public async Task HomeCity_CanBeSetAndCleared_EvenWithoutAProfileYet()
    {
        var city = await _cities.UpsertAsync(Timisoara());

        Assert.True((await _profiles.SetHomeCityAsync(_alice, city.Id)).Success);
        var s = (await _profiles.GetAsync(_alice))!;
        Assert.Equal("Timișoara", s.Profile.HomeCity?.Name);
        Assert.True(s.Profile.AskForGymAfterCheckIn);

        Assert.True((await _profiles.SetHomeCityAsync(_alice, null)).Success);
        Assert.Null((await _profiles.GetAsync(_alice))!.Profile.HomeCityId);
    }

    [Fact] public async Task HomeCity_UnknownCity_IsRejected()
    {
        var r = await _profiles.SetHomeCityAsync(_alice, 999);
        Assert.False(r.Success);
        Assert.Null((await _profiles.GetAsync(_alice))!.Profile.HomeCityId);
    }

    [Fact] public async Task SavingTheBodyDataForm_KeepsTheHomeCity()
    {
        var city = await _cities.UpsertAsync(Timisoara());
        await _profiles.SetHomeCityAsync(_alice, city.Id);

        await _profiles.SaveAsync(_alice, new ProfileInput(Sex.Female, null, 170, null, UnitSystem.Metric));

        Assert.Equal(city.Id, (await _profiles.GetAsync(_alice))!.Profile.HomeCityId);
    }
}
