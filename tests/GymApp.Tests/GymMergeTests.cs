using GymApp.Core;

namespace GymApp.Tests;

public class GymMergeTests
{
    private static FetchedGym F(long id, string name = "Gym", string? address = null, double lat = 45.7, double lon = 21.2,
        OsmElementType type = OsmElementType.Node) => new(type, id, name, address, lat, lon);

    private static StoredOsmGym S(int dbId, long osmId, string name = "Gym", string? address = null, double lat = 45.7,
        double lon = 21.2, bool active = true, OsmElementType type = OsmElementType.Node) =>
        new(dbId, type, osmId, name, address, lat, lon, active);

    [Fact] public void FirstFetch_AddsEverything()
    {
        var plan = GymMerge.Plan([], [F(1), F(2)]);
        Assert.Equal(2, plan.ToAdd.Count);
        Assert.Empty(plan.ToUpdate);
        Assert.Empty(plan.ToDeactivate);
    }

    [Fact] public void SameDataAgain_ChangesNothing()
    {
        var plan = GymMerge.Plan([S(10, 1), S(11, 2)], [F(1), F(2)]);
        Assert.True(plan.IsEmpty);
    }

    [Fact] public void ChangedName_Address_OrPosition_IsUpdated()
    {
        var plan = GymMerge.Plan(
            [S(10, 1), S(11, 2), S(12, 3), S(13, 4)],
            [F(1, name: "Renamed"), F(2, address: "Strada X 1"), F(3, lat: 45.71), F(4)]);

        Assert.Empty(plan.ToAdd);
        Assert.Empty(plan.ToDeactivate);
        Assert.Equal([10, 11, 12], plan.ToUpdate.Select(u => u.Id).Order());
        Assert.All(plan.ToUpdate, u => Assert.False(u.Reactivate));
        Assert.Equal("Renamed", plan.ToUpdate.Single(u => u.Id == 10).Data.Name);
    }

    [Fact] public void MissingFromTheFetch_IsDeactivated_NotDeleted()
    {
        var plan = GymMerge.Plan([S(10, 1), S(11, 2)], [F(1)]);
        Assert.Equal([11], plan.ToDeactivate);
        Assert.Empty(plan.ToAdd);
        Assert.Empty(plan.ToUpdate);
    }

    [Fact] public void AlreadyInactive_AndStillMissing_IsLeftAlone()
    {
        Assert.True(GymMerge.Plan([S(10, 1, active: false)], []).IsEmpty);
    }

    [Fact] public void InactiveGymThatComesBack_IsReactivated_WithTheNewData()
    {
        var plan = GymMerge.Plan([S(10, 1, active: false)], [F(1, name: "Back again")]);
        var u = Assert.Single(plan.ToUpdate);
        Assert.Equal(10, u.Id);
        Assert.True(u.Reactivate);
        Assert.Equal("Back again", u.Data.Name);
        Assert.Empty(plan.ToAdd);
    }

    [Fact] public void InactiveGymThatComesBackUnchanged_IsStillReactivated()
    {
        Assert.True(Assert.Single(GymMerge.Plan([S(10, 1, active: false)], [F(1)]).ToUpdate).Reactivate);
    }

    [Fact] public void MatchesOnTypeAndId_NotIdAlone()
    {
        // node 1 is stored; way 1 is a different object
        var plan = GymMerge.Plan([S(10, 1)], [F(1, type: OsmElementType.Way)]);
        Assert.Single(plan.ToAdd);
        Assert.Equal([10], plan.ToDeactivate);
    }

    [Fact] public void DuplicateInTheFetch_IsAddedOnce()
    {
        Assert.Single(GymMerge.Plan([], [F(1), F(1, name: "dup")]).ToAdd);
    }

    [Fact] public void EmptyFetch_DeactivatesAllActiveGyms()
    {
        var plan = GymMerge.Plan([S(10, 1), S(11, 2, active: false)], []);
        Assert.Equal([10], plan.ToDeactivate);
    }
}
