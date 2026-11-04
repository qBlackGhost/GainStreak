namespace GymApp.Core;

/// <summary>One position from the phone: where it thinks it is and how sure it is (the radius, in metres).</summary>
public record LocationFix(double Latitude, double Longitude, double AccuracyMeters);

/// <summary>A gym the user can be verified at: one with a position (every OpenStreetMap gym, custom gyms with an
/// address).</summary>
public record GymSpot(int GymId, string Name, double Latitude, double Longitude, bool IsCustom);

/// <summary>
/// Phase 18c's "verified at gym": the phone is near one of the user's gyms. Within 150 m of an OpenStreetMap gym or
/// 250 m of a custom one (its address is approximate), plus the fix's own accuracy. A fix less sure than 200 m says
/// nothing. Runs on the phone only: neither the position nor the result leaves it.
/// </summary>
public static class GymProximity
{
    public const double OsmRadiusMeters = 150;
    public const double CustomRadiusMeters = 250;
    public const double WorstAccuracyMeters = 200;

    private const double EarthRadiusMeters = 6_371_000;

    /// <summary>The nearest gym the fix is within reach of, or null (none near, or a poor fix).</summary>
    public static GymSpot? Nearest(LocationFix fix, IEnumerable<GymSpot> gyms)
    {
        if (!IsUsable(fix)) return null;
        var accuracy = Math.Max(0, fix.AccuracyMeters);
        return gyms
            .Select(g => (Gym: g, Distance: DistanceMeters(fix.Latitude, fix.Longitude, g.Latitude, g.Longitude)))
            .Where(x => x.Distance <= (x.Gym.IsCustom ? CustomRadiusMeters : OsmRadiusMeters) + accuracy)
            .OrderBy(x => x.Distance)
            .Select(x => x.Gym)
            .FirstOrDefault();
    }

    /// <summary>A real position, and sure enough to count.</summary>
    public static bool IsUsable(LocationFix fix) =>
        double.IsFinite(fix.Latitude) && double.IsFinite(fix.Longitude) && Math.Abs(fix.Latitude) <= 90
        && Math.Abs(fix.Longitude) <= 180 && double.IsFinite(fix.AccuracyMeters) && fix.AccuracyMeters <= WorstAccuracyMeters;

    /// <summary>The great-circle distance (haversine), in metres.</summary>
    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
