using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace GymApp.Core;

/// <summary>Overpass answered, but reported a problem (usually its own timeout or memory limit). The elements
/// may be cut short, so the response must not be merged: missing gyms would be deactivated by mistake.</summary>
public class OverpassRemarkException(string remark) : Exception(remark);

/// <summary>One gym from an Overpass response, mapped to what the app stores.</summary>
public record FetchedGym(OsmElementType OsmType, long OsmId, string Name, string? Address, double Latitude, double Longitude);

public static class Overpass
{
    public const string UnnamedGym = "Unnamed gym";
    public const int ServerTimeoutSeconds = 25;

    /// <summary>All leisure=fitness_centre objects in the box. nwr + "out center" gives ways and relations
    /// a centre point too, so every result has coordinates.</summary>
    public static string GymQuery(double south, double west, double north, double east) =>
        string.Create(CultureInfo.InvariantCulture,
            $"[out:json][timeout:{ServerTimeoutSeconds}];nwr[\"leisure\"=\"fitness_centre\"]({south},{west},{north},{east});out center tags;");

    /// <summary>Parses an Overpass JSON response into gyms. Elements without a usable type, id or position
    /// are skipped; the same element twice is kept once.</summary>
    /// <exception cref="JsonException">The text is not an Overpass JSON object with an elements array.</exception>
    /// <exception cref="OverpassRemarkException">Overpass added a remark, so the result may be incomplete.</exception>
    public static IReadOnlyList<FetchedGym> ParseGyms(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected an Overpass response object.");
        if (Clean(Str(root, "remark")) is { } remark)
            throw new OverpassRemarkException(remark);
        if (!root.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected an Overpass response with an elements array.");

        var result = new List<FetchedGym>();
        var seen = new HashSet<(OsmElementType, long)>();
        foreach (var e in elements.EnumerateArray())
            if (TryMap(e) is { } gym && seen.Add((gym.OsmType, gym.OsmId)))
                result.Add(gym);
        return result;
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

    private static FetchedGym? TryMap(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (Nominatim.ParseOsmType(Str(e, "type")) is not { } type) return null;
        if (!e.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id)) return null;

        // Nodes have lat/lon; ways and relations have center { lat, lon }
        double? lat = Num(e, "lat"), lon = Num(e, "lon");
        if ((lat is null || lon is null) && e.TryGetProperty("center", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            lat = Num(c, "lat");
            lon = Num(c, "lon");
        }
        if (lat is not { } la || lon is not { } lo) return null;

        string? name = null, street = null, number = null;
        if (e.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
        {
            name = Str(tags, "name");
            street = Str(tags, "addr:street");
            number = Str(tags, "addr:housenumber");
        }

        return new FetchedGym(type, id, Name(name, street), Address(street, number), la, lo);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) &&
        double.IsFinite(d) ? d : null;
}
