using System.Text.Json;
using GymApp.App;
using GymApp.App.ViewModels;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

/// <summary>"Show me gyms near me" on vacation (19d): the rounded city lookup, the visiting city, and the map.</summary>
public sealed class NearbyGymsTests : IDisposable
{
    private const string RomeJson = """
        {"osm_type":"relation","osm_id":41485,"lat":"41.8933203","lon":"12.4829321","addresstype":"city","name":"Roma",
         "display_name":"Roma, Lazio, Italia","boundingbox":["41.6556","42.1410","12.2344","12.8557"]}
        """;
    private const string OneGym = """{"elements":[{"type":"node","id":9,"lat":41.90,"lon":12.50,"tags":{"name":"Palestra"}}]}""";

    private readonly TestDb _db = TestDb.OnDisk();
    private readonly SessionService _session = new();
    private readonly VacationService _vacations;
    private readonly int _me, _home;

    public NearbyGymsTests()
    {
        _vacations = new VacationService(_db, new StreakService(_db), new PointsService(_db));
        _me = _db.AddUser("cristi");
        using var db = _db.CreateDbContext();
        var home = new City { OsmType = OsmElementType.Relation, OsmId = 1, Name = "Timișoara", DisplayName = "Timișoara",
            CenterLatitude = 45.75, CenterLongitude = 21.23, South = 45.7, West = 21.1, North = 45.8, East = 21.35 };
        db.Cities.Add(home);
        db.SaveChanges();
        _home = home.Id;
        db.UserProfiles.Add(new UserProfile { UserId = _me, HomeCityId = home.Id });
        db.UserSchedules.Add(new UserSchedule { UserId = _me, EffectiveFrom = AppClock.Today.AddDays(-10), WeekPattern = "WWWWWWW" });
        db.WorkoutLogs.Add(new WorkoutLog { UserId = _me, Date = AppClock.Today.AddDays(-1) });
        db.SaveChanges();
        _session.SignIn(db.Users.Single(u => u.Id == _me));
    }

    public void Dispose() => _db.Dispose();

    private async Task OnVacationAsync() => Assert.True((await _vacations.StartAsync(_me, AppClock.Today.AddDays(3))).Success);

    private int AddCity(string name, long osmId)
    {
        using var db = _db.CreateDbContext();
        var c = new City { OsmType = OsmElementType.Relation, OsmId = osmId, Name = name, DisplayName = name,
            CenterLatitude = 41.9, CenterLongitude = 12.5, South = 41.6, West = 12.2, North = 42.1, East = 12.8 };
        db.Cities.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    // The lookup

    [Fact]
    public void TheLookup_SendsOnlyARoundedPosition_AtCityLevel()
    {
        var url = GeocodingService.ReverseCityUrl(41.902843, 12.496367);
        Assert.Contains("lat=41.90&lon=12.50", url);
        Assert.Contains("zoom=10", url);
        Assert.DoesNotContain("41.9028", url);
    }

    [Theory]
    [InlineData("city")]
    [InlineData("town")]
    [InlineData("village")]
    public void ParseReverseCity_Settlements(string kind)
    {
        var c = Nominatim.ParseReverseCity(RomeJson.Replace("\"city\"", $"\"{kind}\""))!;
        Assert.Equal("Roma", c.Name);
        Assert.Equal(OsmElementType.Relation, c.OsmType);
        Assert.Equal(42.1410, c.North, 4);
    }

    [Fact]
    public void ParseReverseCity_NotASettlement_OrAnError_IsNull()
    {
        Assert.Null(Nominatim.ParseReverseCity(RomeJson.Replace("\"city\"", "\"county\"")));
        Assert.Null(Nominatim.ParseReverseCity("""{"error":"Unable to geocode"}"""));
        Assert.Throws<JsonException>(() => Nominatim.ParseReverseCity("[]"));
    }

    // The visiting city

    [Fact]
    public async Task VisitingCity_OnlyOnVacation()
    {
        var rome = AddCity("Roma", 41485);
        Assert.False(await _vacations.SetVisitingCityAsync(_me, rome));
        await OnVacationAsync();
        Assert.True(await _vacations.SetVisitingCityAsync(_me, rome));
        Assert.Equal("Roma", (await _vacations.GetVisitingCityAsync(_me))!.Name);
        Assert.Null(await _vacations.GetVisitingCityAsync(_me, AppClock.Today.AddDays(3)));   // back home: gone
    }

    // The finder

    private sealed class FakeLocation(LocationFix? fix) : ILocationProvider
    {
        public bool IsSupported => true;
        public bool HasPermission => true;
        public bool IsPrecise => true;
        public bool NeedsNoteBeforePrompt => false;
        public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
        public Task<LocationFix?> GetFixAsync(TimeSpan timeout) => Task.FromResult(fix);
        public void OpenSettings() { }
    }

    private (NearbyCityFinder Finder, FakeHttpHandler Nominatim) Finder(LocationFix? fix, string reverse = RomeJson)
    {
        var nominatim = FakeHttpHandler.Json(reverse);
        var fetch = new GymFetchService(_db, FakeHttpHandler.Json(OneGym), ["https://overpass.test/api/interpreter"],
            TimeSpan.Zero, TimeSpan.FromMinutes(5), null, TimeSpan.Zero);
        return (new NearbyCityFinder(new FakeLocation(fix), new GeocodingService(nominatim, TimeSpan.Zero), new CityService(_db),
            fetch, _vacations), nominatim);
    }

    [Fact]
    public async Task Finder_FindsTheCity_KeepsIt_AndLoadsItsGyms()
    {
        await OnVacationAsync();
        var (finder, nominatim) = Finder(new LocationFix(41.902843, 12.496367, 20));

        var r = await finder.FindAsync(_me);

        Assert.Equal("Roma", r.City!.Name);
        Assert.Contains("lat=41.90&lon=12.50", Assert.Single(nominatim.Requests).RequestUri!.ToString());
        Assert.Equal(r.City.Id, (await _vacations.GetVisitingCityAsync(_me))!.Id);
        await using var db = _db.CreateDbContext();
        Assert.Contains(await db.Gyms.Where(g => g.CityId == r.City.Id).Select(g => g.Name).ToListAsync(), n => n == "Palestra");
    }

    [Fact]
    public async Task Finder_NoFix_SaysSo_AndSendsNothing()
    {
        var (finder, nominatim) = Finder(null);
        var r = await finder.FindAsync(_me);
        Assert.Null(r.City);
        Assert.Equal(NearbyCityFinder.NoFixText, r.Error);
        Assert.Empty(nominatim.Requests);
    }

    [Fact]
    public async Task Finder_NotInACity_SaysSo()
    {
        var (finder, _) = Finder(new LocationFix(41.9, 12.5, 20), RomeJson.Replace("\"city\"", "\"county\""));
        Assert.Contains("Search for the nearest", (await finder.FindAsync(_me)).Error);
    }

    // The map

    private GymsViewModel Map()
    {
        var gyms = new GymService(_db);
        var profiles = new ProfileService(_db);
        var geocoding = new GeocodingService();
        var picker = new GymPickerViewModel(gyms, new GymFetchService(_db), new GymAddressLookupService(_db, geocoding), geocoding,
            new CityService(_db), profiles, _session, () => null!);
        return new GymsViewModel(gyms, profiles, new FriendMapService(new LocalSocialStore(_db)), new GymSummaryService(_db),
            picker, _session, () => null!, _vacations);
    }

    private static async Task UntilAsync(Func<bool> done)
    {
        for (var i = 0; i < 300 && !done(); i++) await Task.Delay(10);
    }

    [Fact]
    public async Task Map_ShowsTheVisitingCity_WithTheWayBackHome()
    {
        await OnVacationAsync();
        await _vacations.SetVisitingCityAsync(_me, AddCity("Roma", 41485));

        var vm = Map();
        await UntilAsync(() => vm.IsVisiting);
        Assert.Equal("Roma", vm.CityName);
        Assert.Equal("Visiting Roma", vm.VisitingTitle);
        Assert.StartsWith("On vacation until ", vm.VisitingSub);
        Assert.Equal("Back to Timișoara", vm.VisitingButton);

        await vm.ToggleVisitingCommand.ExecuteAsync(null);
        Assert.Equal("Timișoara", vm.CityName);
        Assert.Equal("Show Roma", vm.VisitingButton);
    }

    [Fact]
    public async Task Map_WithoutAVacation_IsTheHomeCity()
    {
        var vm = Map();
        await UntilAsync(() => vm.HasCity);
        Assert.Equal("Timișoara", vm.CityName);
        Assert.False(vm.IsVisiting);
    }
}
