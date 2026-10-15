using GymApp.Core;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

/// <summary>Checks the GymMap migration: the true-by-default bools, the unique indexes and the Restrict on WorkoutLog → Gym.</summary>
public class GymMapSchemaTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly int _alice;
    private readonly int _cityId;

    public GymMapSchemaTests()
    {
        _alice = _db.AddUser("alice");
        using var db = _db.CreateDbContext();
        var city = new City { OsmType = OsmElementType.Relation, OsmId = 1, Name = "Timișoara", DisplayName = "Timișoara, Timiș, România" };
        db.Cities.Add(city);
        db.SaveChanges();
        _cityId = city.Id;
    }

    public void Dispose() => _db.Dispose();

    private Gym Osm(long osmId, string name = "Gym") =>
        new() { OsmType = OsmElementType.Node, OsmId = osmId, CityId = _cityId, Name = name };

    private Gym Custom(string name) => new() { CityId = _cityId, Name = name, OwnerUserId = _alice };

    [Fact] public void Profile_AsksForGymByDefault_ButAnExplicitFalseIsSaved()
    {
        var bob = _db.AddUser("bob");
        using (var db = _db.CreateDbContext())
        {
            db.UserProfiles.Add(new UserProfile { UserId = _alice });
            db.UserProfiles.Add(new UserProfile { UserId = bob, AskForGymAfterCheckIn = false });
            db.SaveChanges();
        }
        using var read = _db.CreateDbContext();
        Assert.True(read.UserProfiles.Single(p => p.UserId == _alice).AskForGymAfterCheckIn);
        Assert.False(read.UserProfiles.Single(p => p.UserId == bob).AskForGymAfterCheckIn);
    }

    [Fact] public void Gym_IsActiveByDefault_ButAnExplicitFalseIsSaved()
    {
        using (var db = _db.CreateDbContext())
        {
            db.Gyms.AddRange(Osm(1, "A"), new Gym { OsmType = OsmElementType.Node, OsmId = 2, CityId = _cityId, Name = "B", IsActive = false });
            db.SaveChanges();
        }
        using var read = _db.CreateDbContext();
        Assert.True(read.Gyms.Single(g => g.Name == "A").IsActive);
        Assert.False(read.Gyms.Single(g => g.Name == "B").IsActive);
    }

    [Fact] public void SameOsmId_WithDifferentType_IsAllowed_ButNotTwiceWithTheSameType()
    {
        using (var db = _db.CreateDbContext())
        {
            db.Gyms.AddRange(Osm(5), new Gym { OsmType = OsmElementType.Way, OsmId = 5, CityId = _cityId, Name = "Gym" });
            db.SaveChanges();
        }
        using var dup = _db.CreateDbContext();
        dup.Gyms.Add(Osm(5));
        Assert.Throws<DbUpdateException>(() => dup.SaveChanges());
    }

    [Fact] public void SameOsmGym_IsAllowedOncePerCity()
    {
        using (var db = _db.CreateDbContext())
        {
            var other = new City { OsmType = OsmElementType.Relation, OsmId = 2, Name = "Dumbrăvița", DisplayName = "Dumbrăvița" };
            db.Cities.Add(other);
            db.SaveChanges();
            db.Gyms.AddRange(Osm(7), new Gym { OsmType = OsmElementType.Node, OsmId = 7, CityId = other.Id, Name = "Gym" });
            db.SaveChanges();
        }
        using var dup = _db.CreateDbContext();
        dup.Gyms.Add(Osm(7));
        Assert.Throws<DbUpdateException>(() => dup.SaveChanges());
    }

    [Fact] public void CustomGymNames_AreUniquePerUserAndCity_IgnoringCase_OsmGymsMayRepeat()
    {
        var bob = _db.AddUser("bob");
        using (var db = _db.CreateDbContext())
        {
            db.Gyms.AddRange(Osm(1, "World Class"), Osm(2, "World Class"), Custom("Home Gym"),
                new Gym { CityId = _cityId, Name = "Home Gym", OwnerUserId = bob });
            db.SaveChanges();
        }
        using var dup = _db.CreateDbContext();
        dup.Gyms.Add(Custom("home gym"));
        Assert.Throws<DbUpdateException>(() => dup.SaveChanges());
    }

    [Fact] public void Gym_UsedByAWorkoutLog_CannotBeDeleted()
    {
        int gymId;
        using (var db = _db.CreateDbContext())
        {
            var gym = Osm(1);
            db.Gyms.Add(gym);
            db.SaveChanges();
            gymId = gym.Id;
            db.WorkoutLogs.Add(new WorkoutLog { UserId = _alice, Date = new DateOnly(2026, 10, 15), GymId = gymId });
            db.SaveChanges();
        }
        using var del = _db.CreateDbContext();
        Assert.ThrowsAny<Exception>(() => del.Gyms.Where(g => g.Id == gymId).ExecuteDelete());
    }
}
