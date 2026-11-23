using System;

namespace GymApp.Core;

/// <summary>
/// How an OSM fitness centre becomes a gym the app stores: the name (or "Unnamed gym (Street)"), and the address from the
/// street and house number. One rule for the app's Overpass fetch and the server's import from a Geofabrik extract, so the
/// two never name the same gym differently.
/// </summary>
public static class OsmGyms
{
    public const string UnnamedGym = "Unnamed gym";

    /// <summary>The gym for an element's tags and position; <paramref name="tag"/> returns a tag's value or null.</summary>
    public static FetchedGym Map(OsmElementType type, long id, double latitude, double longitude, Func<string, string?> tag)
    {
        var street = tag("addr:street");
        return new FetchedGym(type, id, Name(tag("name"), street), Address(street, tag("addr:housenumber")), latitude, longitude);
    }

    /// <summary>"Street 12", or just "Street" without a house number. A house number alone says nothing, so null.</summary>
    public static string? Address(string? street, string? houseNumber)
    {
        street = Clean(street);
        houseNumber = Clean(houseNumber);
        if (street is null) return null;
        return houseNumber is null ? street : $"{street} {houseNumber}";
    }

    /// <summary>The name tag, or "Unnamed gym (Street)" / "Unnamed gym" when there is none.</summary>
    public static string Name(string? name, string? street) =>
        Clean(name) ?? (Clean(street) is { } s ? $"{UnnamedGym} ({s})" : UnnamedGym);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
