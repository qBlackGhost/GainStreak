using GymApp.Core;

namespace GymApp.Data.Models;

/// <summary>A gym from OpenStreetMap (shared, OwnerUserId null) or a user's custom gym (no OSM id, no coordinates).
/// Gyms that a WorkoutLog points to are never deleted, only deactivated.</summary>
public class Gym
{
    public int Id { get; set; }
    public OsmElementType? OsmType { get; set; }      // null for custom gyms
    public long? OsmId { get; set; }
    public int CityId { get; set; }
    public City City { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public double? Latitude { get; set; }             // custom gyms have no coordinates
    public double? Longitude { get; set; }
    public int? OwnerUserId { get; set; }             // null = from OSM, otherwise a custom gym
    public User? Owner { get; set; }
    public bool IsActive { get; set; } = true;
}
