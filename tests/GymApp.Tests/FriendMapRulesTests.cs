using GymApp.Core;

namespace GymApp.Tests;

public class FriendMapRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);
    private const int HomeCity = 1;

    private static FriendCheckIn C(int id, string name, int gymId, DateOnly date,
        int cityId = HomeCity, string city = "Timișoara", bool custom = false, double? km = null, bool noPosition = false) =>
        new(id, name, gymId, $"Gym {gymId}", cityId, city,
            custom || noPosition ? null : 45.75, custom || noPosition ? null : 21.23, km, custom, date);

    // ===== Window and range =====

    [Fact] public void WindowStart_Is29DaysBeforeToday()
    {
        Assert.Equal(Today.AddDays(-29), FriendMapRules.WindowStart(Today));
        Assert.True(FriendMapRules.InRange(Today.AddDays(-29), Today, MapRange.Month));
        Assert.False(FriendMapRules.InRange(Today.AddDays(-30), Today, MapRange.Month));
    }

    [Fact] public void InRange_TodayKeepsOnlyToday_WeekKeepsSixDaysBack_FutureNever()
    {
        Assert.True(FriendMapRules.InRange(Today, Today, MapRange.Today));
        Assert.False(FriendMapRules.InRange(Today.AddDays(-1), Today, MapRange.Today));
        Assert.True(FriendMapRules.InRange(Today.AddDays(-6), Today, MapRange.Week));
        Assert.False(FriendMapRules.InRange(Today.AddDays(-7), Today, MapRange.Week));
        Assert.False(FriendMapRules.InRange(Today.AddDays(1), Today, MapRange.Month));
    }

    // ===== Select =====

    [Fact] public void Latest_KeepsEachFriendsNewest_StableByName()
    {
        var picked = FriendMapRules.Select([
            C(1, "maria_l", 10, Today.AddDays(-3)),
            C(1, "maria_l", 11, Today),
            C(2, "andrei.m", 12, Today),
        ], Today, MapRange.Month, MapMode.Latest);

        Assert.Equal(["andrei.m", "maria_l"], picked.Select(c => c.Username));
        Assert.Equal(11, picked.Single(c => c.FriendUserId == 1).GymId);
    }

    [Fact] public void AllVisits_IsOnePerFriendPerGym_WithTheNewestDate()
    {
        var picked = FriendMapRules.Select([
            C(1, "maria_l", 10, Today.AddDays(-5)),
            C(1, "maria_l", 10, Today.AddDays(-2)),
            C(1, "maria_l", 10, Today.AddDays(-9)),
            C(1, "maria_l", 11, Today),
        ], Today, MapRange.Month, MapMode.AllVisits);

        Assert.Equal(2, picked.Count);
        Assert.Equal(Today.AddDays(-2), picked.Single(c => c.GymId == 10).Date);
    }

    // ===== Layout =====

    [Fact] public void TwoFriendsAtOneGym_ShareAPin_NewestFirst()
    {
        var layout = FriendMapRules.Layout([
            C(1, "andrei.m", 10, Today),
            C(2, "maria_l", 10, Today.AddDays(-3)),
        ], HomeCity, Today, MapRange.Week, MapMode.Latest);

        var pin = Assert.Single(layout.Pins);
        Assert.Equal("andrei.m", pin.Newest.Username);
        Assert.Equal(1, pin.MoreCount);
        Assert.Equal(0, pin.NewestDaysAgo);
    }

    [Fact] public void OtherCities_CustomGyms_AndGymsWithoutAPosition_NeverBecomePins()
    {
        var layout = FriendMapRules.Layout([
            C(1, "andrei.m", 10, Today, cityId: 2, city: "Cluj-Napoca"),
            C(2, "maria_l", 11, Today, custom: true),
            C(3, "vlad_t", 12, Today, noPosition: true),
            C(4, "ioana.p", 13, Today),
        ], HomeCity, Today, MapRange.Month, MapMode.Latest);

        Assert.Equal([13], layout.Pins.Select(p => p.GymId));
        Assert.Equal(4, layout.LatestPerFriend.Count);   // the Friends card still lists everyone
    }

    [Fact] public void NoHomeCity_MeansNoPins()
    {
        var layout = FriendMapRules.Layout([C(1, "andrei.m", 10, Today)], homeCityId: null, Today, MapRange.Month, MapMode.Latest);
        Assert.Empty(layout.Pins);
    }

    [Fact] public void LatestPerFriend_IsTheSameInBothModes()
    {
        FriendCheckIn[] all = [
            C(1, "maria_l", 10, Today.AddDays(-4)),
            C(1, "maria_l", 11, Today.AddDays(-1)),
            C(2, "andrei.m", 10, Today.AddDays(-2)),
        ];
        var latest = FriendMapRules.Layout(all, HomeCity, Today, MapRange.Month, MapMode.Latest).LatestPerFriend;
        var all2 = FriendMapRules.Layout(all, HomeCity, Today, MapRange.Month, MapMode.AllVisits);

        Assert.Equal(latest, all2.LatestPerFriend);
        Assert.Equal(3, all2.Pins.Sum(p => p.Visitors.Count));   // all visits: maria at both gyms
    }

    // ===== Friends card =====

    [Fact] public void Recent_SplitsTodayThisWeekEarlier_AndAwayGoesToElsewhere()
    {
        var groups = FriendMapRules.GroupFriends([
            C(1, "a", 10, Today),
            C(2, "b", 10, Today.AddDays(-1)),
            C(3, "c", 10, Today.AddDays(-6)),
            C(4, "d", 10, Today.AddDays(-7)),
            C(5, "e", 20, Today, cityId: 2, city: "Cluj-Napoca"),
            C(6, "f", 21, Today, custom: true),
        ], FriendSort.Recent, HomeCity, homeGymId: null, Today);

        Assert.Equal([FriendGroupKind.Today, FriendGroupKind.ThisWeek, FriendGroupKind.Earlier, FriendGroupKind.Elsewhere],
            groups.Select(g => g.Kind));
        Assert.Equal(["b", "c"], groups[1].Rows.Select(r => r.Username));
        Assert.Equal(["e", "f"], groups[3].Rows.Select(r => r.Username));
        Assert.False(groups[3].OnMap);
        Assert.True(groups[0].OnMap);
    }

    [Fact] public void Recent_LeavesOutEmptyGroups_AndRowsAreNewestFirst()
    {
        var groups = FriendMapRules.GroupFriends([
            C(1, "a", 10, Today.AddDays(-5)),
            C(2, "b", 10, Today.AddDays(-2)),
        ], FriendSort.Recent, HomeCity, null, Today);

        var g = Assert.Single(groups);
        Assert.Equal(FriendGroupKind.ThisWeek, g.Kind);
        Assert.Equal(["b", "a"], g.Rows.Select(r => r.Username));
    }

    [Fact] public void ByGym_OrdersNearestFirst_MarksYourGym_AndPutsOtherCitiesLast()
    {
        var groups = FriendMapRules.GroupFriends([
            C(1, "andrei.m", 10, Today, km: 1.1),
            C(2, "vlad_t", 11, Today.AddDays(-1), km: 0.5),
            C(3, "maria_l", 99, Today.AddDays(-4), cityId: 2, city: "Cluj-Napoca"),
            C(4, "ioana.p", 12, Today),                       // no distance: after the known ones
            C(5, "radu99", 98, Today, cityId: 3, city: "Arad"),
        ], FriendSort.ByGym, HomeCity, homeGymId: 10, Today);

        Assert.Equal(["Gym 11", "Gym 10 · your gym", "Gym 12", "Arad", "Cluj-Napoca"], groups.Select(g => g.Title));
        Assert.Equal(0.5, groups[0].DistanceKm);
        Assert.False(groups[^1].OnMap);
        Assert.Equal(FriendGroupKind.City, groups[^1].Kind);
    }

    [Fact] public void ByGym_RowsInAGroupAreNewestFirst()
    {
        var groups = FriendMapRules.GroupFriends([
            C(1, "a", 10, Today.AddDays(-3), km: 1),
            C(2, "b", 10, Today, km: 1),
        ], FriendSort.ByGym, HomeCity, null, Today);

        Assert.Equal(["b", "a"], Assert.Single(groups).Rows.Select(r => r.Username));
    }

    [Fact] public void ByGym_UnknownDistancesComeLast_SortedByName()
    {
        var groups = FriendMapRules.GroupFriends([
            new(1, "a", 30, "Zeta Gym", HomeCity, "Timișoara", 45.75, 21.23, null, false, Today),
            new(2, "b", 31, "Alpha Gym", HomeCity, "Timișoara", 45.75, 21.23, null, false, Today),
            C(3, "c", 32, Today, km: 4.2),
        ], FriendSort.ByGym, HomeCity, null, Today);

        Assert.Equal(["Gym 32", "Alpha Gym", "Zeta Gym"], groups.Select(g => g.Title));
    }

    [Fact] public void Layout_EveryHomeCityGymWithAPositionIsAPin()
    {
        var layout = FriendMapRules.Layout([
            C(1, "a", 10, Today),
            C(2, "b", 11, Today.AddDays(-2)),
        ], HomeCity, Today, MapRange.Month, MapMode.Latest);

        Assert.Equal([10, 11], layout.Pins.Select(p => p.GymId).Order());
        Assert.All(layout.Pins, p => Assert.Equal(0, p.MoreCount));
    }

    // ===== Small helpers =====

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1, 0.85)]
    [InlineData(2, 0.85)]
    [InlineData(3, 0.6)]
    [InlineData(7, 0.6)]
    [InlineData(8, 0.35)]
    [InlineData(29, 0.35)]
    public void Opacity_FadesWithAge(int daysAgo, double expected) =>
        Assert.Equal(expected, FriendMapRules.Opacity(daysAgo));

    [Fact] public void DescribeDay_SaysTheDayNeverATime()
    {
        Assert.Equal("Today", FriendMapRules.DescribeDay(Today, Today));
        Assert.Equal("Yesterday", FriendMapRules.DescribeDay(Today.AddDays(-1), Today));
        Assert.Equal("2 days ago", FriendMapRules.DescribeDay(Today.AddDays(-2), Today));
        Assert.Equal("6 days ago", FriendMapRules.DescribeDay(Today.AddDays(-6), Today));
        Assert.Equal("12 Oct", FriendMapRules.DescribeDay(Today.AddDays(-7), Today));
    }

    [Theory]
    [InlineData("andrei.m", "AN")]
    [InlineData("vlad_t", "VL")]
    [InlineData("12345", "?")]
    [InlineData("x9", "X")]
    public void Initials_UseTheFirstTwoLetters(string username, string expected) =>
        Assert.Equal(expected, FriendMapRules.Initials(username));

    [Fact] public void DistanceKm_IsZeroForTheSamePoint_AndAbout111KmPerDegreeOfLatitude()
    {
        Assert.Equal(0, FriendMapRules.DistanceKm(45.75, 21.23, 45.75, 21.23), 6);
        Assert.InRange(FriendMapRules.DistanceKm(45, 21, 46, 21), 110.5, 111.8);
    }
}
