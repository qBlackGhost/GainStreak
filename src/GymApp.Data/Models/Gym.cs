using System;
using GymApp.Core;

namespace GymApp.Data.Models;

/// <summary>A gym from OpenStreetMap (shared, OwnerUserId null) or a user's custom gym (no OSM id; a position only if its address was picked).
/// An OSM gym has one row per city whose box contains it. Gyms that a WorkoutLog points to are never deleted, only deactivated.</summary>
public class Gym
{
    public int Id { get; set; }
    public OsmElementType? OsmType { get; set; }      // null for custom gyms
    public long? OsmId { get; set; }
    public int CityId { get; set; }
    public City City { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? Address { get; set; }             // from OSM tags, or the address a user picked for a custom gym
    public string? LookedUpAddress { get; set; }     // approximate, from a reverse lookup, for OSM gyms with no Address
    public DateTime? AddressLookedUpAt { get; set; } // set once the lookup ran (even if it found nothing), so it runs once
    public double? Latitude { get; set; }             // custom gyms only have them when their address was picked
    public double? Longitude { get; set; }
    public int? OwnerUserId { get; set; }             // null = from OSM, otherwise a custom gym
    public User? Owner { get; set; }
    public bool IsActive { get; set; } = true;
}
