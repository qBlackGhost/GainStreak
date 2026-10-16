using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GymApp.Core;

public enum GymSort { Name, Distance }

/// <summary>A gym as the picker lists it. AddressIsApproximate = OSM had no address, so it came from a reverse
/// lookup of the gym's position ("near ..."). Custom gyms only have coordinates when their address was picked.</summary>
public record GymListItem(int Id, string Name, string? Address, double? Latitude, double? Longitude, bool IsCustom,
    bool AddressIsApproximate = false);

/// <summary>A listed gym with its distance from the city centre (null when it has no coordinates).</summary>
public record GymRow(GymListItem Gym, double? DistanceKm);

public static class Geo
{
    private const double EarthRadiusKm = 6371.0088;   // mean radius

    /// <summary>Great-circle distance in km between two points in degrees.</summary>
    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double deg) => deg * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>"350 m" under 1 km, "1.2 km" up to 10 km, then "12 km".</summary>
    public static string FormatDistance(double km) =>
        km < 1 ? $"{Math.Round(km * 1000 / 10) * 10:0} m"
        : km < 10 ? km.ToString("0.0 'km'", CultureInfo.InvariantCulture)
        : km.ToString("0 'km'", CultureInfo.InvariantCulture);
}

public static class GymList
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);

    /// <summary>Case-insensitive name match that also ignores accents, so "timisoara" finds "Timișoara".
    /// An empty search matches everything.</summary>
    public static bool Matches(string name, string? search) =>
        string.IsNullOrWhiteSpace(search) ||
        Compare.IndexOf(name, search.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    /// <summary>Filters by name and sorts. By distance: nearest first, then gyms without coordinates
    /// (custom gyms) by name. Same-name gyms keep a stable order by id.</summary>
    public static IReadOnlyList<GymRow> Arrange(IEnumerable<GymListItem> gyms, string? search, GymSort sort,
        double centerLatitude, double centerLongitude)
    {
        var rows = gyms
            .Where(g => Matches(g.Name, search))
            .Select(g => new GymRow(g, g is { Latitude: { } lat, Longitude: { } lon }
                ? Geo.HaversineKm(centerLatitude, centerLongitude, lat, lon)
                : null));

        var ordered = sort == GymSort.Distance
            ? rows.OrderBy(r => r.DistanceKm is null).ThenBy(r => r.DistanceKm ?? 0).ThenBy(r => r.Gym.Name, NameOrder)
            : rows.OrderBy(r => r.Gym.Name, NameOrder);
        return ordered.ThenBy(r => r.Gym.Id).ToList();
    }
}
