using GymApp.Core;

namespace GymApp.Server.Data;

/// <summary>
/// A shared gym check-in: the gym and the day of one workout log, never a time. Gyms and cities are identified by their
/// OSM type and id, because a phone's local ids mean nothing on another phone. One per user per day, like workout logs.
/// Only stored while the user shares check-ins, and deleted after 31 days.
/// </summary>
public class CheckIn
{
    public const int MaxNameLength = 200;

    public int UserId { get; set; }
    public DateOnly Date { get; set; }

    /// <summary>Null, with <see cref="GymOsmId"/>, for a custom gym.</summary>
    public OsmElementType? GymOsmType { get; set; }
    public long? GymOsmId { get; set; }
    public string GymName { get; set; } = "";

    public OsmElementType CityOsmType { get; set; }
    public long CityOsmId { get; set; }
    public string CityName { get; set; } = "";

    /// <summary>Always null for a custom gym: its position may be someone's home.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
