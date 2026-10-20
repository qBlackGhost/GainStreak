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

/// <summary>One day of <see cref="CheckInsUploadRequest"/>: the date and the same gym fields as <see cref="CheckInRequest"/>.</summary>
public sealed record DatedCheckInRequest(
    DateOnly Date,
    OsmType? GymOsmType, long? GymOsmId, string GymName,
    OsmType CityOsmType, long CityOsmId, string CityName,
    double? Latitude, double? Longitude);

/// <summary>
/// Replaces every check-in the server holds for you (<c>PUT /v1/me/checkins</c>). At most 31 entries, no date twice.
/// Entries older than the server keeps are dropped rather than refused, so a phone can always send its last 31 days.
/// An empty list clears everything.
/// </summary>
public sealed record CheckInsUploadRequest(IReadOnlyList<DatedCheckInRequest> CheckIns);

/// <summary>A friend's check-in: gym and day, never a time. Coordinates are null for a custom gym.</summary>
public sealed record FriendCheckInResponse(
    int UserId, string Username, DateOnly Date,
    OsmType? GymOsmType, long? GymOsmId, string GymName,
    OsmType CityOsmType, long CityOsmId, string CityName,
    double? Latitude, double? Longitude);
