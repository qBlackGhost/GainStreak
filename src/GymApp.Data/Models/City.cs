using System;
using GymApp.Core;

namespace GymApp.Data.Models;

/// <summary>A city the user picked from a Nominatim search. Shared across users; the OSM type and id are unique together.
/// The bounding box is what the gym fetch searches inside.</summary>
public class City
{
    public int Id { get; set; }
    public OsmElementType OsmType { get; set; }
    public long OsmId { get; set; }
    public string Name { get; set; } = "";             // e.g. "Timișoara"
    public string DisplayName { get; set; } = "";      // with county and country, to tell same-named cities apart
    public double CenterLatitude { get; set; }
    public double CenterLongitude { get; set; }
    public double South { get; set; }
    public double West { get; set; }
    public double North { get; set; }
    public double East { get; set; }
    public DateTime? GymsFetchedAt { get; set; }       // last successful gym fetch; null = never fetched
}
