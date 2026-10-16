using GymApp.Core;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.Tests;

public class CustomGymServiceTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 16);

    private readonly TestDb _db = new();
    private readonly GymService _svc;
    private readonly int _alice;
    private readonly int _bob;
    private readonly int _timisoara;
    private readonly int _arad;

    public CustomGymServiceTests()
    {
        _svc = new GymService(_db);
        _alice = _db.AddUser("alice");
        _bob = _db.AddUser("bob");
        _timisoara = AddCity(1, "Timișoara");
        _arad = AddCity(2, "Arad");
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

    private static PickedAddress At(string text, double lat = 45.75, double lon = 21.22) => new(text, lat, lon);

    private async Task<int> Add(string name, int? userId = null, int? cityId = null, PickedAddress? address = null)
    {
        var r = await _svc.AddCustomGymAsync(userId ?? _alice, cityId ?? _timisoara, name, address);
        Assert.True(r.Success, r.Error);
        return r.GymId!.Value;
    }

    private Gym? Gym(int id)
    {
        using var db = _db.CreateDbContext();
        return db.Gyms.SingleOrDefault(g => g.Id == id);
    }

    private void Log(int userId, DateOnly date, int gymId)
    {
        using var db = _db.CreateDbContext();
        db.WorkoutLogs.Add(new WorkoutLog { UserId = userId, Date = date, GymId = gymId });
        db.SaveChanges();
    }

    // ===== Add =====

    [Fact] public async Task Add_WithoutAnAddress_HasNoPosition_AndSortsLast()
    {
        var id = await Add("No address");
        Assert.Null(Gym(id)!.Latitude);
        Assert.Null(Gym(id)!.Address);
    }

    [Fact] public async Task Add_WithAPickedAddress_StoresItsPosition_SoItHasADistance()
    {
        var id = await Add("Garage", address: At("Calea Aradului 1, Circumvalațiunii", 45.78, 21.22));

        var g = Gym(id)!;
        Assert.Equal(("Calea Aradului 1, Circumvalațiunii", 45.78, 21.22), (g.Address, g.Latitude, g.Longitude));
        var row = Assert.Single((await _svc.GetGymsForCityAsync(_alice, _timisoara, sort: GymSort.Distance))!.Rows);
        Assert.NotNull(row.DistanceKm);
        Assert.False(row.Gym.AddressIsApproximate);
    }

    [Fact] public async Task Add_AddressWithABadPosition_IsRejected()
    {
        Assert.False((await _svc.AddCustomGymAsync(_alice, _timisoara, "A", At("Strada X", double.NaN))).Success);
        Assert.False((await _svc.AddCustomGymAsync(_alice, _timisoara, "A", At("Strada X", 95))).Success);
        Assert.False((await _svc.AddCustomGymAsync(_alice, _timisoara, "A", At("   "))).Success);
    }

    [Fact] public async Task Add_SavesACustomGym_AndOnlyTheOwnerSeesIt()
    {
        var id = await Add("  Garage   Gym ", address: At(" Strada X 1 "));

        var g = Gym(id)!;
        Assert.Equal(("Garage Gym", "Strada X 1"), (g.Name, g.Address));
        Assert.Equal(_alice, g.OwnerUserId);
        Assert.Equal(_timisoara, g.CityId);
        Assert.True(g.IsActive);
        Assert.Null(g.OsmId);

        var row = Assert.Single((await _svc.GetGymsForCityAsync(_alice, _timisoara))!.Rows);
        Assert.True(row.Gym.IsCustom);
        Assert.Empty((await _svc.GetGymsForCityAsync(_bob, _timisoara))!.Rows);   // only the owner sees it
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_EmptyName_IsRejected(string name) =>
        Assert.False((await _svc.AddCustomGymAsync(_alice, _timisoara, name, null)).Success);

    [Fact] public async Task Add_NameOver60Characters_IsRejected_60IsFine()
    {
        Assert.False((await _svc.AddCustomGymAsync(_alice, _timisoara, new string('a', 61), null)).Success);
        Assert.True((await _svc.AddCustomGymAsync(_alice, _timisoara, new string('a', 60), null)).Success);
    }

    [Fact] public async Task Add_UnknownCity_IsRejected() =>
        Assert.False((await _svc.AddCustomGymAsync(_alice, 999, "Gym", null)).Success);

    [Fact] public async Task Add_AtMostTenPerUser_AcrossCities()
    {
        for (var i = 1; i <= 9; i++) await Add($"Gym {i}");
        await Add("Gym 10", cityId: _arad);

        var r = await _svc.AddCustomGymAsync(_alice, _timisoara, "Gym 11", null);
        Assert.False(r.Success);
        Assert.Contains("10", r.Error);

        await Add("Bob's first", userId: _bob);   // the limit is per user
    }

    [Fact] public async Task Add_AfterDeletingOne_IsAllowedAgain()
    {
        var ids = new List<int>();
        for (var i = 1; i <= 10; i++) ids.Add(await Add($"Gym {i}"));
        Log(_alice, Today, ids[0]);

        await _svc.DeleteCustomGymAsync(_alice, ids[0]);   // deactivated (used), no longer counts
        Assert.True((await _svc.AddCustomGymAsync(_alice, _timisoara, "Gym 11", null)).Success);
    }

    [Theory]
    [InlineData("home gym")]
    [InlineData("HOME GYM")]
    [InlineData("  Home   Gym ")]
    public async Task Add_SameNameInTheSameCity_IgnoringCase_IsRejected(string again)
    {
        await Add("Home Gym");
        var r = await _svc.AddCustomGymAsync(_alice, _timisoara, again, null);
        Assert.False(r.Success);
        Assert.Contains("already", r.Error);
    }

    [Fact] public async Task Add_SameNameWithNonAsciiCase_IsRejected()
    {
        await Add("Știința");
        Assert.False((await _svc.AddCustomGymAsync(_alice, _timisoara, "știința", null)).Success);
    }

    [Fact] public async Task Add_SameName_InAnotherCity_OrByAnotherUser_IsFine()
    {
        await Add("Home Gym");
        await Add("Home Gym", cityId: _arad);
        await Add("Home Gym", userId: _bob);
    }

    [Fact] public async Task Add_SameNameAsAGym_KeptOnlyForHistory_BringsThatGymBack()
    {
        var id = await Add("Home Gym", address: At("old"));
        Log(_alice, Today, id);
        await _svc.DeleteCustomGymAsync(_alice, id);   // deactivated

        var r = await _svc.AddCustomGymAsync(_alice, _timisoara, "home gym", At("new"));

        Assert.True(r.Success);
        Assert.True(r.Reactivated);
        Assert.Equal(id, r.GymId);
        var g = Gym(id)!;
        Assert.True(g.IsActive);
        Assert.Equal(("home gym", "new"), (g.Name, g.Address));
    }

    // ===== Edit =====

    [Fact] public async Task Edit_ChangesNameAndAddress_AndClearingTheAddressClearsThePosition()
    {
        var id = await Add("Old", address: At("Somewhere"));

        Assert.True((await _svc.UpdateCustomGymAsync(_alice, id, "Old", At("Elsewhere 2", 45.70, 21.30))).Success);
        Assert.Equal(("Elsewhere 2", 45.70), (Gym(id)!.Address, Gym(id)!.Latitude));

        Assert.True((await _svc.UpdateCustomGymAsync(_alice, id, " New  name ", null)).Success);
        var g = Gym(id)!;
        Assert.Equal(("New name", null, null), (g.Name, g.Address, g.Latitude));
    }

    [Fact] public async Task Edit_KeepAddress_LeavesAnOlderHandTypedAddressAlone()
    {
        int id;
        using (var db = _db.CreateDbContext())
        {
            // A custom gym from before addresses were picked: typed text, no position
            var g = new Gym { CityId = _timisoara, Name = "World Class Isho", Address = "Isho", OwnerUserId = _alice };
            db.Gyms.Add(g);
            db.SaveChanges();
            id = g.Id;
        }

        Assert.True((await _svc.UpdateCustomGymAsync(_alice, id, "World Class ISHO", null, keepAddress: true)).Success);

        var saved = Gym(id)!;
        Assert.Equal(("World Class ISHO", "Isho", null), (saved.Name, saved.Address, saved.Latitude));
    }

    [Fact] public async Task Edit_ChangingOnlyTheCase_IsFine()
    {
        var id = await Add("home gym");
        Assert.True((await _svc.UpdateCustomGymAsync(_alice, id, "Home Gym", null)).Success);
    }

    [Fact] public async Task Edit_ToAnotherOfMyNamesInThatCity_IsRejected()
    {
        await Add("A");
        var b = await Add("B");
        Assert.False((await _svc.UpdateCustomGymAsync(_alice, b, "a", null)).Success);
        Assert.Equal("B", Gym(b)!.Name);
    }

    [Fact] public async Task Edit_SomeoneElsesGym_IsRejected()
    {
        var bobs = await Add("Bob's", userId: _bob);
        Assert.False((await _svc.UpdateCustomGymAsync(_alice, bobs, "Mine now", null)).Success);
        Assert.Equal("Bob's", Gym(bobs)!.Name);
    }

    [Fact] public async Task Edit_AnOsmGym_IsRejected()
    {
        int osmId;
        using (var db = _db.CreateDbContext())
        {
            var g = new Gym { CityId = _timisoara, Name = "World Class", OsmType = OsmElementType.Node, OsmId = 5, Latitude = 1, Longitude = 1 };
            db.Gyms.Add(g);
            db.SaveChanges();
            osmId = g.Id;
        }
        Assert.False((await _svc.UpdateCustomGymAsync(_alice, osmId, "Mine", null)).Success);
        Assert.False((await _svc.DeleteCustomGymAsync(_alice, osmId)).Success);
        Assert.True(Gym(osmId)!.IsActive);
    }

    [Fact] public async Task Edit_InvalidName_IsRejected()
    {
        var id = await Add("A");
        Assert.False((await _svc.UpdateCustomGymAsync(_alice, id, " ", null)).Success);
        Assert.False((await _svc.UpdateCustomGymAsync(_alice, id, new string('x', 61), null)).Success);
    }

    // ===== Delete =====

    [Fact] public async Task Delete_UnusedGym_IsDeleted()
    {
        var id = await Add("Unused");
        var r = await _svc.DeleteCustomGymAsync(_alice, id);
        Assert.Equal(CustomGymDeleteOutcome.Deleted, r.Outcome);
        Assert.Null(Gym(id));
    }

    [Fact] public async Task Delete_UsedGym_IsDeactivated_LeavesThePicker_StaysOnTheLog()
    {
        var id = await Add("Used");
        Log(_alice, Today.AddDays(-2), id);

        var r = await _svc.DeleteCustomGymAsync(_alice, id);

        Assert.True(r.Success);
        Assert.Equal(CustomGymDeleteOutcome.Deactivated, r.Outcome);
        Assert.False(Gym(id)!.IsActive);
        Assert.Empty((await _svc.GetGymsForCityAsync(_alice, _timisoara))!.Rows);
        using var db = _db.CreateDbContext();
        Assert.Equal(id, db.WorkoutLogs.Single().GymId);
    }

    [Fact] public async Task Delete_UsedGym_IsNoLongerOfferedAsTheLastGym()
    {
        var id = await Add("Used");
        Log(_alice, Today.AddDays(-1), id);
        await _svc.DeleteCustomGymAsync(_alice, id);
        Assert.Null(await _svc.GetLastUsedGymAsync(_alice));
    }

    [Fact] public async Task Delete_SomeoneElsesGym_IsRejected()
    {
        var bobs = await Add("Bob's", userId: _bob);
        Assert.False((await _svc.DeleteCustomGymAsync(_alice, bobs)).Success);
        Assert.NotNull(Gym(bobs));
    }
}
