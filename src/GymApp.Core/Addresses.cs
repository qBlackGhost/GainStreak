using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace GymApp.Core;

/// <summary>A street address found by searching inside a city. HouseNumber is null when OSM only knows the street;
/// the user can then add one. Latitude/Longitude are the result's position.</summary>
public record AddressCandidate(string Road, string? HouseNumber, string? Area, string DisplayName, double Latitude, double Longitude)
{
    public string Text => Addresses.Format(Road, HouseNumber, Area);
    public bool HasHouseNumber => HouseNumber is not null;
}

public static class Addresses
{
    public const int MaxHouseNumberLength = 10;
    public const int MaxCandidates = 5;
    public const int RequestLimit = 10;   // duplicates (several buildings at one address) get merged, so ask for more

    // The street part of a Nominatim address, most specific first
    private static readonly string[] RoadKeys = ["road", "pedestrian", "footway", "square", "place", "path", "cycleway"];
    // The area that tells same-named streets or gyms apart; suburb is the name people know best
    private static readonly string[] AreaKeys = ["suburb", "quarter", "neighbourhood", "city_district"];

    /// <summary>"Calea Aradului 1, Circumvalațiunii", "Strada X, Elisabetin", or just "Strada X".</summary>
    public static string Format(string road, string? houseNumber, string? area)
    {
        var street = string.IsNullOrWhiteSpace(houseNumber) ? road.Trim() : $"{road.Trim()} {houseNumber.Trim()}";
        return string.IsNullOrWhiteSpace(area) ? street : $"{street}, {area.Trim()}";
    }

    /// <summary>Null when fine. House numbers are short and must contain a digit ("12", "8-10", "3A").</summary>
    public static string? CheckHouseNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return null;
        var n = number.Trim();
        if (n.Length > MaxHouseNumberLength) return "That house number is too long.";
        foreach (var ch in n) if (char.IsDigit(ch)) return null;
        return "A house number needs at least one digit, e.g. 12 or 8-10.";
    }

    /// <summary>The address of a reverse lookup (format=jsonv2, addressdetails=1), or null when Nominatim found
    /// nothing or no street there. Used for gyms OSM has no address for, so it's approximate.</summary>
    /// <exception cref="JsonException">Not a JSON object.</exception>
    public static string? ParseReverse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a JSON object.");
        if (!root.TryGetProperty("address", out var a) || a.ValueKind != JsonValueKind.Object) return null;   // "Unable to geocode"
        if (First(a, RoadKeys) is not { } road) return null;
        return Format(road, Str(a, "house_number"), First(a, AreaKeys));
    }

    /// <summary>Street addresses from a bounded /search (format=jsonv2, addressdetails=1). Results without a street
    /// or position are skipped; the same address twice (e.g. two buildings at one number) is kept once.</summary>
    /// <exception cref="JsonException">Not a JSON array.</exception>
    public static IReadOnlyList<AddressCandidate> ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Expected a JSON array.");

        var result = new List<AddressCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            if (!e.TryGetProperty("address", out var a) || a.ValueKind != JsonValueKind.Object) continue;
            if (First(a, RoadKeys) is not { } road) continue;
            if (Num(e, "lat") is not { } lat || Num(e, "lon") is not { } lon) continue;

            var c = new AddressCandidate(road, Str(a, "house_number"), First(a, AreaKeys),
                Str(e, "display_name")?.Trim() ?? "", lat, lon);
            if (!seen.Add(c.Text)) continue;
            result.Add(c);
            if (result.Count == MaxCandidates) break;
        }
        return result;
    }

    private static string? First(JsonElement e, string[] keys)
    {
        foreach (var k in keys)
            if (Str(e, k) is { } v) return v;
        return null;
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static double? Num(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String &&
        double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)
            ? d
            : null;
}
