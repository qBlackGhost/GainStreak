using GymApp.Core;

namespace GymApp.Tests;

public class GymSummaryTests
{
    private static DateOnly D(int day, int month = 10) => new(2026, month, day);

    private static VisitedGym Osm(long id, string name, bool active = true, string? address = null) =>
        new(VisitedGym.OsmKey(OsmElementType.Node, id), name, address, active, false);

    private static VisitedGym Custom(int id, string name) => new(VisitedGym.CustomKey(id), name, null, true, true);

    private static MonthSummary Oct(params MonthLog[] logs) => GymSummaries.Build(2026, 10, logs);

    [Fact] public void EmptyMonth()
    {
        var s = Oct();
        Assert.True(s.IsEmpty);
        Assert.Empty(s.Gyms);
        Assert.Equal((0, 0, 0), (s.DistinctGyms, s.TotalWorkouts, s.NoGymLogged));
    }

    [Fact] public void OnlyNoGymLogs()
    {
        var s = Oct(new(D(1), null), new(D(2), null), new(D(5), null));
        Assert.Empty(s.Gyms);
        Assert.Equal((0, 3, 3), (s.DistinctGyms, s.TotalWorkouts, s.NoGymLogged));
    }

    [Fact] public void SeveralGyms_SortedByVisitsThenName()
    {
        var wc = Osm(1, "World Class");
        var befit = Osm(2, "Befit");
        var arena = Osm(3, "arena");
        var garage = Custom(9, "Garage");
        var s = Oct(
            new(D(1), wc), new(D(2), befit), new(D(3), wc), new(D(4), garage),
            new(D(5), arena), new(D(6), null), new(D(7), wc), new(D(8), befit));

        Assert.Equal(["World Class", "Befit", "arena", "Garage"], s.Gyms.Select(g => g.Name));
        Assert.Equal([3, 2, 1, 1], s.Gyms.Select(g => g.Visits));
        Assert.Equal((4, 8, 1), (s.DistinctGyms, s.TotalWorkouts, s.NoGymLogged));
        Assert.True(s.Gyms.Single(g => g.Name == "Garage").IsCustom);
    }

    [Fact] public void InactiveGym_IsStillCountedAndNamed()
    {
        var closed = Osm(1, "Closed Gym", active: false);
        var s = Oct(new(D(1), closed), new(D(2), closed), new(D(3), Osm(2, "Open Gym")));

        var g = s.Gyms[0];
        Assert.Equal(("Closed Gym", 2, false), (g.Name, g.Visits, g.IsActive));
        Assert.Equal(2, s.DistinctGyms);
    }

    [Fact] public void SameOsmGym_PickedInTwoCities_CountsAsOneGym()
    {
        // One row per city, same OSM node: e.g. picked once via Timișoara, once via Dumbrăvița
        var viaTimisoara = Osm(42, "World Class");
        var viaDumbravita = Osm(42, "World Class");
        var s = Oct(new(D(1), viaTimisoara), new(D(2), viaDumbravita));

        var g = Assert.Single(s.Gyms);
        Assert.Equal(2, g.Visits);
        Assert.Equal(1, s.DistinctGyms);
    }

    [Fact] public void SameName_DifferentGyms_StaySeparate()
    {
        var s = Oct(new(D(1), Osm(1, "World Class", address: "Lipovei")), new(D(2), Osm(2, "World Class", address: "Bastion")));
        Assert.Equal(2, s.DistinctGyms);
        Assert.Equal(["Lipovei", "Bastion"], s.Gyms.Select(g => g.Address));   // tie on visits and name: stable by key
    }

    [Fact] public void ShowsTheLatestNameOfARenamedGym()
    {
        var s = Oct(new(D(1), Osm(1, "Old Name")), new(D(20), Osm(1, "New Name")), new(D(5), Osm(1, "Old Name")));
        Assert.Equal("New Name", Assert.Single(s.Gyms).Name);
    }

    [Fact] public void LogsOutsideTheMonth_AreIgnored()
    {
        var s = Oct(new(D(30, 9), Osm(1, "A")), new(D(1), Osm(1, "A")), new(D(31), null), new(D(1, 11), null));
        Assert.Equal((1, 2, 1), (s.DistinctGyms, s.TotalWorkouts, s.NoGymLogged));
    }

    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2026, 12, 31)]
    [InlineData(2026, 4, 30)]
    public void MonthRange_IsTheCalendarMonth(int year, int month, int lastDay)
    {
        var (first, last) = GymSummaries.MonthRange(year, month);
        Assert.Equal(new DateOnly(year, month, 1), first);
        Assert.Equal(new DateOnly(year, month, lastDay), last);
    }

    [Theory]
    [InlineData(2026, 10, false)]
    [InlineData(2026, 9, false)]
    [InlineData(2025, 12, false)]
    [InlineData(2026, 11, true)]
    [InlineData(2027, 1, true)]
    public void FutureMonths(int year, int month, bool future) =>
        Assert.Equal(future, GymSummaries.IsFuture(year, month, new DateOnly(2026, 10, 16)));

    [Fact] public void Shift_CrossesYears()
    {
        Assert.Equal((2025, 12), GymSummaries.Shift(2026, 1, -1));
        Assert.Equal((2027, 1), GymSummaries.Shift(2026, 12, 1));
    }

    [Fact] public void MonthTitle() => Assert.Equal("October 2026", GymSummaries.MonthTitle(2026, 10));
}
