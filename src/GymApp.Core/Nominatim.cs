using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace GymApp.Core;

/// <summary>One city from a Nominatim search. DisplayName includes the county and country,
/// so cities with the same name can be told apart.</summary>
public record CityCandidate(
    OsmElementType OsmType,
    long OsmId,
    string Name,
    string DisplayName,
    double CenterLatitude,
    double CenterLongitude,
    double South,
    double West,
    double North,
    double East);

public static class Nominatim
{
    public const int MaxCandidates = 5;

    /// <summary>How many results to ask for, so there are still enough left after counties and duplicates are dropped.</summary>
    public const int RequestLimit = 10;

    // featureType=settlement still returns counties ("Arad" the county next to Arad the city), so keep only
    // places people live in. Filtered on addresstype, not rank: Bucharest is a city at the counties' rank.
    private static readonly HashSet<string> SettlementTypes =
        new(StringComparer.OrdinalIgnoreCase) { "city", "town", "village", "municipality", "hamlet" };

    /// <summary>Parses a Nominatim /search response (format=jsonv2). Keeps only cities, towns and villages
    /// (no counties or regions), skips entries with a missing or broken id, centre or bounding box, and
    /// drops duplicates: the same OSM object twice, or a town's centre point next to its own boundary.</summary>
    /// <exception cref="JsonException">The text is not a JSON array.</exception>
    public static IReadOnlyList<CityCandidate> ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected a JSON array of search results.");

        var all = new List<CityCandidate>();
        var seen = new HashSet<(OsmElementType, long)>();
        foreach (var e in doc.RootElement.EnumerateArray())
            if (TryParse(e) is { } c && seen.Add((c.OsmType, c.OsmId)))
                all.Add(c);

        var result = new List<CityCandidate>();
        foreach (var c in all)
        {
            if (IsPointOfAnArea(c, all)) continue;
            result.Add(c);
            if (result.Count == MaxCandidates) break;
        }
        return result;
    }

    /// <summary>A town often comes back twice: its centre point (a node, with a made-up box around it) and its
    /// boundary (a relation or way, with the real box). Same name and the point inside the boundary = same town;
    /// keep the boundary, since its box is what the gym search uses.</summary>
    private static bool IsPointOfAnArea(CityCandidate c, List<CityCandidate> all) =>
        c.OsmType == OsmElementType.Node && all.Exists(o =>
            o.OsmType != OsmElementType.Node &&
            string.Equals(o.Name, c.Name, StringComparison.OrdinalIgnoreCase) &&
            c.CenterLatitude >= o.South && c.CenterLatitude <= o.North &&
            c.CenterLongitude >= o.West && c.CenterLongitude <= o.East);

    public static OsmElementType? ParseOsmType(string? type) => type?.ToLowerInvariant() switch
    {
        "node" or "n" => OsmElementType.Node,
        "way" or "w" => OsmElementType.Way,
        "relation" or "r" => OsmElementType.Relation,
        _ => null
    };

    private static CityCandidate? TryParse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (Str(e, "addresstype") is { } kind && !SettlementTypes.Contains(kind)) return null;
        if (ParseOsmType(Str(e, "osm_type")) is not { } type) return null;
        if (!TryLong(e, "osm_id", out var id)) return null;

        if (Num(e, "lat") is not { } lat || Num(e, "lon") is not { } lon) return null;

        // boundingbox is [south, north, west, east], as strings
        if (!e.TryGetProperty("boundingbox", out var bb) || bb.ValueKind != JsonValueKind.Array || bb.GetArrayLength() != 4)
            return null;
        if (Num(bb[0]) is not { } south || Num(bb[1]) is not { } north ||
            Num(bb[2]) is not { } west || Num(bb[3]) is not { } east)
            return null;

        var display = Str(e, "display_name")?.Trim() ?? "";
        var name = Str(e, "name")?.Trim();
        if (string.IsNullOrEmpty(name)) name = display.Split(',')[0].Trim();
        if (name.Length == 0) return null;
        if (display.Length == 0) display = name;

        return new CityCandidate(type, id, name, display, lat, lon, south, west, north, east);
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) ? Num(v) : null;

    // Nominatim sends coordinates as strings; accept plain numbers too
    private static double? Num(JsonElement v)
    {
        double d;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out d)) return double.IsFinite(d) ? d : null;
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            return double.IsFinite(d) ? d : null;
        return null;
    }

    private static bool TryLong(JsonElement e, string prop, out long value)
    {
        value = 0;
        if (!e.TryGetProperty(prop, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out value);
        return v.ValueKind == JsonValueKind.String &&
               long.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
