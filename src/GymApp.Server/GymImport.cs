using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server;

/// <summary>A map box: south, west, north, east.</summary>
public readonly record struct GeoBox(double South, double West, double North, double East);

/// <summary>What an import did.</summary>
public record GymImportResult(int Added, int Updated, int Deactivated, int Total);

/// <summary>
/// Loads a country's gyms into <see cref="ServerDbContext.OsmGyms"/> (0.27.0) from a Geofabrik extract:
/// <c>osmium tags-filter ... nwr/leisure=fitness_centre</c>, then <c>osmium export -f geojson -a type,id</c>, then
/// <see cref="ParseGeoJson"/> and <see cref="ApplyAsync"/>. The same tag rule as the app's Overpass fetch (<see cref="OsmGyms"/>).
/// </summary>
public static class GymImport
{
    /// <summary>The gyms in an <c>osmium export -a type,id</c> GeoJSON. A way or relation gets the centre of its bounding box,
    /// as Overpass's "out center" does. The export can list a feature twice (a closed way as a line and as an area): kept once.
    /// Features without a type, an id or coordinates are skipped.</summary>
    public static IReadOnlyList<FetchedGym> ParseGeoJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected a GeoJSON FeatureCollection.");

        var result = new List<FetchedGym>();
        var seen = new HashSet<(OsmElementType, long)>();
        foreach (var f in features.EnumerateArray())
        {
            if (!f.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object) continue;
            if (Nominatim.ParseOsmType(Str(props, "@type")) is not { } type) continue;
            if (!props.TryGetProperty("@id", out var idEl) || !idEl.TryGetInt64(out var id)) continue;
            if (!f.TryGetProperty("geometry", out var geometry) || Centre(geometry) is not { } centre) continue;
            if (!seen.Add((type, id))) continue;
            result.Add(OsmGyms.Map(type, id, centre.Lat, centre.Lon, key => Str(props, key)));
        }
        return result;
    }

    /// <summary>A point's position, or the centre of the bounding box of every position in the geometry.</summary>
    private static (double Lat, double Lon)? Centre(JsonElement geometry)
    {
        if (geometry.ValueKind != JsonValueKind.Object || !geometry.TryGetProperty("coordinates", out var coords)) return null;
        double minLat = double.MaxValue, maxLat = double.MinValue, minLon = double.MaxValue, maxLon = double.MinValue;
        var any = false;
        void Walk(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Array) return;
            if (e.GetArrayLength() >= 2 && e[0].ValueKind == JsonValueKind.Number && e[1].ValueKind == JsonValueKind.Number)
            {
                double lon = e[0].GetDouble(), lat = e[1].GetDouble();
                if (!double.IsFinite(lat) || !double.IsFinite(lon)) return;
                minLat = Math.Min(minLat, lat); maxLat = Math.Max(maxLat, lat);
                minLon = Math.Min(minLon, lon); maxLon = Math.Max(maxLon, lon);
                any = true;
                return;
            }
            foreach (var child in e.EnumerateArray()) Walk(child);
        }
        Walk(coords);
        return any ? ((minLat + maxLat) / 2, (minLon + maxLon) / 2) : null;
    }

    /// <summary>Upserts a country's gyms and its box in one transaction: new ones are added, known ones updated (and made
    /// active again), and the country's gyms missing from this import made inactive. A failure leaves the old rows as they were.</summary>
    public static async Task<GymImportResult> ApplyAsync(ServerDbContext db, string country, IReadOnlyList<FetchedGym> gyms,
        GeoBox box, DateTime dataAsOf, DateTime now)
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        var stored = await db.OsmGyms.Where(g => g.Country == country).ToDictionaryAsync(g => (g.OsmType, g.OsmId));
        // A gym near a border may already be stored from another country's import: this import takes it over
        var incoming = gyms.Select(g => (Type: ToContract(g.OsmType), g)).ToList();
        var otherKeys = incoming.Where(i => !stored.ContainsKey((i.Type, i.g.OsmId))).Select(i => i.g.OsmId).ToHashSet();
        foreach (var g in await db.OsmGyms.Where(g => g.Country != country && otherKeys.Contains(g.OsmId)).ToListAsync())
            stored.TryAdd((g.OsmType, g.OsmId), g);

        int added = 0, updated = 0;
        var seen = new HashSet<(OsmType, long)>();
        foreach (var (type, g) in incoming)
        {
            if (!seen.Add((type, g.OsmId))) continue;
            var name = Trim(g.Name, OsmGym.MaxNameLength)!;
            var address = Trim(g.Address, OsmGym.MaxAddressLength);
            if (stored.TryGetValue((type, g.OsmId), out var row))
            {
                if (row.Name != name || row.Address != address || row.Latitude != g.Latitude || row.Longitude != g.Longitude
                    || !row.IsActive || row.Country != country)
                    updated++;
                row.Name = name; row.Address = address; row.Latitude = g.Latitude; row.Longitude = g.Longitude;
                row.IsActive = true; row.Country = country; row.UpdatedAt = now;
            }
            else
            {
                db.OsmGyms.Add(new OsmGym
                {
                    OsmType = type, OsmId = g.OsmId, Name = name, Address = address, Latitude = g.Latitude,
                    Longitude = g.Longitude, IsActive = true, Country = country, UpdatedAt = now,
                });
                added++;
            }
        }

        var deactivated = 0;
        foreach (var row in stored.Values.Where(r => r.Country == country && r.IsActive && !seen.Contains((r.OsmType, r.OsmId))))
        {
            row.IsActive = false;
            row.UpdatedAt = now;
            deactivated++;
        }

        var c = await db.OsmGymCountries.FindAsync(country);
        if (c is null) db.OsmGymCountries.Add(c = new OsmGymCountry { Country = country });
        (c.South, c.West, c.North, c.East) = (box.South, box.West, box.North, box.East);
        c.DataAsOf = dataAsOf;
        c.ImportedAt = now;
        c.GymCount = seen.Count;

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(added, updated, deactivated, seen.Count);
    }

    /// <summary>The box and the data's date from <c>osmium fileinfo -j</c>: the header's first box and its replication
    /// timestamp. Null parts when the header doesn't have them.</summary>
    public static (GeoBox? Box, DateTime? DataAsOf) ParseFileInfo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("header", out var header)) return (null, null);
        GeoBox? box = null;
        if (header.TryGetProperty("boxes", out var boxes) && boxes.ValueKind == JsonValueKind.Array && boxes.GetArrayLength() > 0
            && boxes[0] is { ValueKind: JsonValueKind.Array } b && b.GetArrayLength() == 4)
            box = new GeoBox(South: b[1].GetDouble(), West: b[0].GetDouble(), North: b[3].GetDouble(), East: b[2].GetDouble());
        DateTime? asOf = null;
        if (header.TryGetProperty("option", out var option) && option.ValueKind == JsonValueKind.Object)
            foreach (var key in new[] { "osmosis_replication_timestamp", "timestamp" })
                if (Str(option, key) is { } t && DateTime.TryParse(t, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    asOf = parsed;
                    break;
                }
        return (box, asOf);
    }

    /// <summary>The smallest box around the gyms, for a file whose header has none.</summary>
    public static GeoBox? Extent(IReadOnlyList<FetchedGym> gyms) => gyms.Count == 0 ? null
        : new GeoBox(gyms.Min(g => g.Latitude), gyms.Min(g => g.Longitude), gyms.Max(g => g.Latitude), gyms.Max(g => g.Longitude));

    private static OsmType ToContract(OsmElementType t) => t switch
    {
        OsmElementType.Node => OsmType.Node,
        OsmElementType.Way => OsmType.Way,
        _ => OsmType.Relation,
    };

    private static string? Trim(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// <c>import-gyms [--country romania] [--file path.osm.pbf]</c>: downloads the country's Geofabrik extract (or reads
/// <c>--file</c>), filters it with osmium (installed in the server image) and loads its gyms. Runs as a Cloud Run Job, weekly.
/// <c>--country</c> is a Geofabrik path under europe/ ("romania") or a full one ("europe/hungary").
/// </summary>
public static class GymImportCommand
{
    public const string Name = "import-gyms";
    public const string GeofabrikBase = "https://download.geofabrik.de/";

    public static bool IsCommand(string[] args) => args is [Name, ..];

    public static async Task<(bool Ok, string Message)> RunAsync(string[] args, ServerDbContext db, TimeProvider clock)
    {
        string region = "europe/romania";
        string? file = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--country" && i + 1 < args.Length) { var c = args[++i].Trim('/'); region = c.Contains('/') ? c : $"europe/{c}"; }
            else if (args[i] == "--file" && i + 1 < args.Length) file = args[++i];
            else return (false, $"Usage: {Name} [--country romania] [--file path.osm.pbf]");
        }
        var country = region[(region.LastIndexOf('/') + 1)..].ToLowerInvariant();
        if (country.Length is 0 or > OsmGym.MaxCountryLength) return (false, "That country name isn't usable.");

        var work = Directory.CreateTempSubdirectory("gyms-");
        try
        {
            if (file is null)
            {
                file = Path.Combine(work.FullName, "extract.osm.pbf");
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
                await using var download = await http.GetStreamAsync($"{GeofabrikBase}{region}-latest.osm.pbf");
                await using var target = File.Create(file);
                await download.CopyToAsync(target);
            }
            else if (!File.Exists(file)) return (false, $"No file at {file}.");

            var filtered = Path.Combine(work.FullName, "gyms.osm.pbf");
            var geojson = Path.Combine(work.FullName, "gyms.geojson");
            var (box, asOf) = GymImport.ParseFileInfo(await OsmiumAsync("fileinfo", "-j", file));
            await OsmiumAsync("tags-filter", file, "nwr/leisure=fitness_centre", "-o", filtered, "--overwrite");
            await OsmiumAsync("export", filtered, "-f", "geojson", "-a", "type,id", "-o", geojson, "--overwrite");

            var gyms = GymImport.ParseGeoJson(await File.ReadAllTextAsync(geojson));
            if (gyms.Count == 0) return (false, "The extract has no gyms; nothing was changed.");
            var now = clock.GetUtcNow().UtcDateTime;
            var r = await GymImport.ApplyAsync(db, country, gyms, box ?? GymImport.Extent(gyms)!.Value, asOf ?? now, now);
            return (true, $"Imported {r.Total} gyms for {country} (data as of {(asOf ?? now):u}): " +
                          $"{r.Added} added, {r.Updated} updated, {r.Deactivated} made inactive.");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or InvalidOperationException
                                   or System.ComponentModel.Win32Exception)
        {
            return (false, $"Import failed, nothing was changed: {e.Message}");
        }
        finally
        {
            try { work.Delete(recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Runs osmium and returns its standard output; throws when it fails.</summary>
    private static async Task<string> OsmiumAsync(params string[] args)
    {
        var start = new ProcessStartInfo("osmium") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start) ?? throw new InvalidOperationException("Couldn't start osmium.");
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException($"osmium {args[0]} failed: {(await error).Trim()}");
        return await output;
    }
}
