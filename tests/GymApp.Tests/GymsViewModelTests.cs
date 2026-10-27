using GymApp.Core;
using GymApp.Data.Models;
using GymApp.App.ViewModels;
using GymApp.Services;

namespace GymApp.Tests;

public class GymsViewModelTests : IDisposable
{
    private readonly TestDb _db = TestDb.OnDisk();
    private readonly SessionService _session = new();
    private readonly int _me, _city, _gym;

    public GymsViewModelTests()
    {
        _me = _db.AddUser("cristi");
        using var db = _db.CreateDbContext();
        var city = new City
        {
            OsmType = OsmElementType.Relation, OsmId = 1, Name = "Timișoara", DisplayName = "Timișoara",
            CenterLatitude = 45.75, CenterLongitude = 21.23, South = 45.7, West = 21.1, North = 45.8, East = 21.35
        };
        db.Cities.Add(city);
        db.SaveChanges();
        _city = city.Id;
        _gym = AddGym("World Class", 7);
        db.UserProfiles.Add(new UserProfile { UserId = _me, HomeCityId = city.Id });
        db.SaveChanges();
        _session.SignIn(db.Users.Single(u => u.Id == _me));
    }

    public void Dispose() => _db.Dispose();

    private int AddGym(string name, long osmId)
    {
        using var db = _db.CreateDbContext();
        var gym = new Gym { CityId = _city, Name = name, OsmType = OsmElementType.Node, OsmId = osmId, Latitude = 45.76, Longitude = 21.22 };
        db.Gyms.Add(gym);
        db.SaveChanges();
        return gym.Id;
    }

    private void LogToday(int? gymId)
    {
        using var db = _db.CreateDbContext();
        var log = db.WorkoutLogs.FirstOrDefault(w => w.UserId == _me && w.Date == AppClock.Today);
        if (log is null) db.WorkoutLogs.Add(new WorkoutLog { UserId = _me, Date = AppClock.Today, GymId = gymId });
        else log.GymId = gymId;
        db.SaveChanges();
    }

    private GymsViewModel NewMap()
    {
        var gyms = new GymService(_db);
        var profiles = new ProfileService(_db);
        var geocoding = new GeocodingService();
        var picker = new GymPickerViewModel(gyms, new GymFetchService(_db), new GymAddressLookupService(_db, geocoding), geocoding,
            new CityService(_db), profiles, _session, () => null!);
        return new GymsViewModel(gyms, profiles, new FriendMapService(new LocalSocialStore(_db)), new GymSummaryService(_db),
            picker, _session, () => null!);
    }

    private static async Task LoadedAsync(GymsViewModel vm)
    {
        for (var i = 0; i < 200 && vm.GymDots.Count == 0; i++) await Task.Delay(10);
    }

    [Theory, InlineData(true), InlineData(false)]
    public async Task YourPin_ShowsAtTodaysGym_WhateverTheSharingSetting(bool share)
    {
        LogToday(_gym);
        Assert.True((await new ProfileService(_db).SetShareGymCheckInsAsync(_me, share)).Success);

        var vm = NewMap();
        await LoadedAsync(vm);

        Assert.Null(vm.Error);
        Assert.Equal(_gym, vm.MyPin?.GymId);
    }

    [Fact] public async Task TogglingSharing_ReloadsTheMap_IncludingGymsFetchedSinceItOpened()
    {
        LogToday(null);
        var vm = NewMap();
        await LoadedAsync(vm);
        Assert.Null(vm.MyPin);

        // Meanwhile the picker fetched a new gym and today's log got it
        var newGym = AddGym("Stay Fit", 8);
        LogToday(newGym);

        vm.ShareCheckIns = true;
        for (var i = 0; i < 200 && vm.MyPin is null; i++) await Task.Delay(10);

        Assert.Equal(newGym, vm.MyPin?.GymId);
        Assert.Null(vm.Error);
    }
}
