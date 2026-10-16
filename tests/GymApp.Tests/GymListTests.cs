using GymApp.Core;

namespace GymApp.Tests;

public class GymListTests
{
    // Timișoara centre (Piața Victoriei area)
    private const double CenterLat = 45.7538355, CenterLon = 21.2257474;

    private static GymListItem G(int id, string name, double? lat = null, double? lon = null, bool custom = false) =>
        new(id, name, null, lat, lon, custom);

    [Fact] public void Haversine_SamePoint_IsZero() =>
        Assert.Equal(0, Geo.HaversineKm(45.75, 21.22, 45.75, 21.22), 9);

    [Fact] public void Haversine_TimisoaraToArad_IsAbout47Km()
    {
        // Arad city centre: 46.1754, 21.3196. Straight-line distance is ~47.4 km.
        Assert.InRange(Geo.HaversineKm(CenterLat, CenterLon, 46.1753793, 21.3196342), 46.9, 47.9);
    }

    [Fact] public void Haversine_OneDegreeOfLatitude_IsAbout111Km() =>
        Assert.InRange(Geo.HaversineKm(45, 21, 46, 21), 111.0, 111.4);

    [Fact] public void Haversine_IsSymmetric() =>
        Assert.Equal(Geo.HaversineKm(45.7, 21.2, 46.1, 21.3), Geo.HaversineKm(46.1, 21.3, 45.7, 21.2), 9);

    [Theory]
    [InlineData(0.347, "350 m")]
    [InlineData(0.004, "0 m")]
    [InlineData(1.234, "1.2 km")]
    [InlineData(9.94, "9.9 km")]
    [InlineData(12.6, "13 km")]
    public void FormatDistance(double km, string expected) => Assert.Equal(expected, Geo.FormatDistance(km));

    [Fact] public void SortByName_IgnoresCase()
    {
        var rows = GymList.Arrange([G(1, "world Class"), G(2, "Befit"), G(3, "Arena")], null, GymSort.Name, CenterLat, CenterLon);
        Assert.Equal(["Arena", "Befit", "world Class"], rows.Select(r => r.Gym.Name));
    }

    [Fact] public void SortByDistance_NearestFirst_GymsWithoutCoordinatesLastByName()
    {
        var rows = GymList.Arrange(
        [
            G(1, "Far", 45.80, 21.30),
            G(2, "Zed custom", custom: true),
            G(3, "Near", 45.754, 21.226),
            G(4, "Abc custom", custom: true),
            G(5, "Middle", 45.76, 21.24)
        ], null, GymSort.Distance, CenterLat, CenterLon);

        Assert.Equal(["Near", "Middle", "Far", "Abc custom", "Zed custom"], rows.Select(r => r.Gym.Name));
        Assert.True(rows[0].DistanceKm < 0.1);
        Assert.Null(rows[3].DistanceKm);
    }

    [Fact] public void SameName_KeepsAStableOrderById()
    {
        var rows = GymList.Arrange([G(9, "World Class"), G(4, "World Class")], null, GymSort.Name, CenterLat, CenterLon);
        Assert.Equal([4, 9], rows.Select(r => r.Gym.Id));
    }

    [Theory]
    [InlineData("world", 2)]
    [InlineData("WORLD class", 2)]
    [InlineData("  gym  ", 1)]
    [InlineData("stiinta", 1)]    // accents ignored: matches "Știința Fitness"
    [InlineData("xyz", 0)]
    [InlineData("", 4)]
    [InlineData(null, 4)]
    public void Search_IsCaseAndAccentInsensitive(string? search, int expected)
    {
        var gyms = new[] { G(1, "World Class"), G(2, "world class Iulius"), G(3, "Gym One"), G(4, "Știința Fitness") };
        Assert.Equal(expected, GymList.Arrange(gyms, search, GymSort.Name, CenterLat, CenterLon).Count);
    }

    [Fact] public void DistanceIsFilledEvenWhenSortingByName()
    {
        var row = Assert.Single(GymList.Arrange([G(1, "A", 45.76, 21.24)], null, GymSort.Name, CenterLat, CenterLon));
        Assert.NotNull(row.DistanceKm);
    }
}
