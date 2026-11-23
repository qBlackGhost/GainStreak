namespace GymApp.Contracts;

/// <summary>A gym from the server's copy of OpenStreetMap (0.27.0), the same fields the app takes from Overpass.</summary>
public sealed record ServerGym(OsmType OsmType, long OsmId, string Name, string? Address, double Latitude, double Longitude);

/// <summary>The active gyms in a map box (<c>GET /v1/gyms?south=&amp;west=&amp;north=&amp;east=</c>). <see cref="Covered"/> is
/// true when the box lies inside a country the server has loaded recently: an empty list then really means no gyms, so the
/// app needn't ask Overpass. <see cref="DataAsOf"/> is when that country's OSM data was taken (null when not covered).</summary>
public sealed record GymBoxResponse(IReadOnlyList<ServerGym> Gyms, bool Covered, DateTime? DataAsOf);
