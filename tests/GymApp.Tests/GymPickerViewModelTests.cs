using System.Net;
using System.Text;
using GymApp.Core;
using GymApp.Data.Models;
using GymApp.App.ViewModels;
using GymApp.Services;

namespace GymApp.Tests;

public class GymPickerViewModelTests : IDisposable
{
    private const string Endpoint = "https://main.test/api/interpreter";
    private const string OneGym = """{"elements":[{"type":"node","id":1,"lat":45.75,"lon":21.22,"tags":{"name":"World Class"}}]}""";

    private readonly TestDb _db = TestDb.OnDisk();
    private readonly SessionService _session = new();
    private readonly City _city;

    public GymPickerViewModelTests()
    {
        var me = _db.AddUser("cristi");
        using var db = _db.CreateDbContext();
        _city = new City
        {
            OsmType = OsmElementType.Relation, OsmId = 1, Name = "Timișoara", DisplayName = "Timișoara",
            CenterLatitude = 45.75, CenterLongitude = 21.22, South = 45.68, West = 21.11, North = 45.83, East = 21.33
        };
        db.Cities.Add(_city);
        db.SaveChanges();
        _session.SignIn(db.Users.Single(u => u.Id == me));
    }

    public void Dispose() => _db.Dispose();

    private GymPickerViewModel Picker(FakeHttpHandler handler)
    {
        var gyms = new GymService(_db);
        var geocoding = new GeocodingService(FakeHttpHandler.Json("[]"), TimeSpan.Zero);
        var fetch = new GymFetchService(_db, handler, [Endpoint], TimeSpan.Zero, TimeSpan.FromMinutes(5), null, TimeSpan.Zero);
        return new GymPickerViewModel(gyms, fetch, new GymAddressLookupService(_db, geocoding), geocoding,
            new CityService(_db), new ProfileService(_db), _session, () => null!)
        {
            SlowFetchAfter = TimeSpan.FromMilliseconds(100)
        };
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact] public async Task SlowFetch_SaysSo_AndTryAgain_SendsAFreshRequest()
    {
        var calls = 0;
        var handler = new FakeHttpHandler(async (_, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1) await Task.Delay(Timeout.Infinite, ct);   // the busy server never answers
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OneGym, Encoding.UTF8, "application/json") };
        });
        var vm = Picker(handler);

        vm.Open(PickerMode.HomeGym, _city, null, null, null, new Dictionary<int, int>(), (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask, () => { });
        await Until(() => vm.IsFetchSlow);
        Assert.True(vm.IsFetching);
        Assert.Equal("Still loading. The map servers are busy right now.", vm.FetchingText);

        await vm.RestartFetchCommand.ExecuteAsync(null);

        await Until(() => !vm.IsFetching);
        Assert.False(vm.IsFetchSlow);
        Assert.Null(vm.FetchError);
        Assert.Equal(2, calls);
        Assert.Equal(["World Class"], vm.Gyms.Select(g => g.Name));
    }

    [Fact] public async Task QuickFetch_NeverShowsTheSlowMessage()
    {
        var vm = Picker(FakeHttpHandler.Json(OneGym));
        vm.Open(PickerMode.HomeGym, _city, null, null, null, new Dictionary<int, int>(), (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask, () => { });

        await Until(() => !vm.IsFetching && vm.Gyms.Count == 1);
        await Task.Delay(200);   // past SlowFetchAfter
        Assert.False(vm.IsFetchSlow);
    }
}
