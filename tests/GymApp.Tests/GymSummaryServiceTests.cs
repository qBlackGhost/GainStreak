using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

public class GymSummaryServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly GymSummaryService _svc;
    private readonly int _alice;
    private readonly int _bob;
    private readonly int _timisoara;
    private readonly int _dumbravita;

    public GymSummaryServiceTests()
    {
        _svc = new GymSummaryService(_db);
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
        _timisoara = AddCity(1, "Timișoara");
        _dumbravita = AddCity(2, "Dumbrăvița");
    }

    public void Dispose() => _db.Dispose();

    private int AddCity(long osmId, string name)
    {
        using var db = _db.CreateDbContext();
        var c = new City { OsmType = OsmElementType.Relation, OsmId = osmId, Name = name, DisplayName = name };
        db.Cities.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    private int AddGym(string name, int cityId, long? osmId = null, int? owner = null, bool active = true,
        string? address = null, string? lookedUp = null)
    {
        using var db = _db.CreateDbContext();
        var g = new Gym
        {
            CityId = cityId, Name = name, OwnerUserId = owner, IsActive = active, Address = address, LookedUpAddress = lookedUp,
            OsmType = owner is null ? OsmElementType.Node : null, OsmId = owner is null ? osmId : null
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

    private static DateOnly D(int day, int month = 10) => new(2026, month, day);

    [Fact] public async Task EmptyMonth() =>
        Assert.True((await _svc.GetMonthAsync(_alice, 2026, 10)).IsEmpty);

    [Fact] public async Task CountsTheMonth_OnlyMine_AndInactiveAndCustomGyms()
    {
        var wc = AddGym("World Class", _timisoara, osmId: 1, lookedUp: "Piața Consiliul Europei, Lipovei");
        var closed = AddGym("Closed", _timisoara, osmId: 2, active: false);
        var garage = AddGym("Garage", _timisoara, owner: _alice, address: "Strada X 1");
        Log(_alice, D(1), wc);
        Log(_alice, D(2), wc);
        Log(_alice, D(3), closed);
        Log(_alice, D(4), garage);
        Log(_alice, D(5));
        Log(_alice, D(30, 9), wc);   // another month
        Log(_bob, D(1), wc);         // another user

        var s = await _svc.GetMonthAsync(_alice, 2026, 10);

        Assert.Equal((3, 5, 1), (s.DistinctGyms, s.TotalWorkouts, s.NoGymLogged));
        Assert.Equal(["World Class", "Closed", "Garage"], s.Gyms.Select(g => g.Name));
        Assert.Equal("Piața Consiliul Europei, Lipovei", s.Gyms[0].Address);
        Assert.False(s.Gyms.Single(g => g.Name == "Closed").IsActive);
        Assert.True(s.Gyms.Single(g => g.Name == "Garage").IsCustom);
    }

    [Fact] public async Task SameOsmGymInTwoCities_IsOneGym()
    {
        var a = AddGym("World Class", _timisoara, osmId: 42);
        var b = AddGym("World Class", _dumbravita, osmId: 42);
        Log(_alice, D(1), a);
        Log(_alice, D(2), b);

        var s = await _svc.GetMonthAsync(_alice, 2026, 10);

        Assert.Equal(2, Assert.Single(s.Gyms).Visits);
    }
}
