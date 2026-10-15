using System.Collections.Generic;
using System.Linq;

namespace GymApp.Core;

/// <summary>An OSM gym already stored for the city. Custom gyms have no OSM id, so they can't be passed in
/// and the merge can never touch them.</summary>
public record StoredOsmGym(int Id, OsmElementType OsmType, long OsmId, string Name, string? Address,
    double? Latitude, double? Longitude, bool IsActive);

/// <summary>A stored gym that gets the fetched data. Reactivate = it was inactive and came back.</summary>
public record GymUpdate(int Id, FetchedGym Data, bool Reactivate);

/// <summary>What to do with a city's OSM gyms after a fetch. Nothing is ever deleted.</summary>
public record GymMergePlan(IReadOnlyList<FetchedGym> ToAdd, IReadOnlyList<GymUpdate> ToUpdate, IReadOnlyList<int> ToDeactivate)
{
    public bool IsEmpty => ToAdd.Count == 0 && ToUpdate.Count == 0 && ToDeactivate.Count == 0;
}

public static class GymMerge
{
    /// <summary>Matches on OSM type and id. Gyms still there get the new name, address and position (and are
    /// reactivated if they were inactive); new ones are added; active ones missing from the fetch are
    /// deactivated. Unchanged gyms are left out of the plan.</summary>
    public static GymMergePlan Plan(IEnumerable<StoredOsmGym> stored, IEnumerable<FetchedGym> fetched)
    {
        var byKey = new Dictionary<(OsmElementType, long), StoredOsmGym>();
        foreach (var s in stored) byKey.TryAdd((s.OsmType, s.OsmId), s);

        var add = new List<FetchedGym>();
        var update = new List<GymUpdate>();
        var seen = new HashSet<(OsmElementType, long)>();

        foreach (var f in fetched)
        {
            var key = (f.OsmType, f.OsmId);
            if (!seen.Add(key)) continue;

            if (!byKey.TryGetValue(key, out var s)) { add.Add(f); continue; }
            var changed = s.Name != f.Name || s.Address != f.Address || s.Latitude != f.Latitude || s.Longitude != f.Longitude;
            if (changed || !s.IsActive) update.Add(new GymUpdate(s.Id, f, Reactivate: !s.IsActive));
        }

        var deactivate = byKey.Values
            .Where(s => s.IsActive && !seen.Contains((s.OsmType, s.OsmId)))
            .Select(s => s.Id)
            .ToList();

        return new GymMergePlan(add, update, deactivate);
    }
}
