using GymApp.Contracts;

namespace GymApp.Server.Data;

/// <summary>A fitness centre from a Geofabrik extract (0.27.0), served to the app instead of Overpass. Keyed by its OSM element;
/// one missing from a later import of its country stays, inactive, so the app can deactivate it too.</summary>
public class OsmGym
{
    public const int MaxNameLength = 200;
    public const int MaxAddressLength = 300;
    public const int MaxCountryLength = 60;

    public OsmType OsmType { get; set; }
    public long OsmId { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>The import it came from ("romania"), so a country's import only deactivates its own gyms.</summary>
    public string Country { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A country whose gyms are loaded: its box (a request inside it is "covered") and when its OSM data was taken.</summary>
public class OsmGymCountry
{
    public string Country { get; set; } = "";
    public double South { get; set; }
    public double West { get; set; }
    public double North { get; set; }
    public double East { get; set; }
    public DateTime DataAsOf { get; set; }
    public DateTime ImportedAt { get; set; }
    public int GymCount { get; set; }
}
