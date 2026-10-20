namespace GymApp.Contracts;

/// <summary>The kind of OpenStreetMap object a gym or city came from; OSM ids are only unique per type.
/// Sent as <c>node</c>, <c>way</c> or <c>relation</c>.</summary>
public enum OsmType { Node, Way, Relation }

public sealed record SharingRequest(bool ShareGymCheckIns);

/// <summary>
/// The gym of one day's workout log. A custom gym has no OSM type and id; any coordinates sent with it are dropped.
/// The city is always sent. The date is in the URL (<c>/v1/me/checkins/yyyy-MM-dd</c>).
/// </summary>
public sealed record CheckInRequest(
    OsmType? GymOsmType, long? GymOsmId, string GymName,
    OsmType CityOsmType, long CityOsmId, string CityName,
    double? Latitude, double? Longitude);

/// <summary>A friend's check-in: gym and day, never a time. Coordinates are null for a custom gym.</summary>
public sealed record FriendCheckInResponse(
    int UserId, string Username, DateOnly Date,
    OsmType? GymOsmType, long? GymOsmId, string GymName,
    OsmType CityOsmType, long CityOsmId, string CityName,
    double? Latitude, double? Longitude);
