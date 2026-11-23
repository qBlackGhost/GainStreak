using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Core;

namespace GymApp.Services;

/// <summary>The server's answer for a box: its gyms, and whether the box lies in a country it has loaded recently (then an empty
/// list really means no gyms).</summary>
public record ServerGymsAnswer(IReadOnlyList<FetchedGym> Gyms, bool Covered);

/// <summary>Gyms from our own server (0.27.0), asked before Overpass. Null when it can't answer (offline, refused, signed out).</summary>
public interface IServerGyms
{
    Task<ServerGymsAnswer?> GetAsync(double south, double west, double north, double east, CancellationToken ct = default);
}

/// <summary>The server's <c>GET /v1/gyms</c> through the app's API client.</summary>
public class RemoteServerGyms(SocialApiClient api) : IServerGyms
{
    public async Task<ServerGymsAnswer?> GetAsync(double south, double west, double north, double east, CancellationToken ct = default)
    {
        var r = await api.GetGymsAsync(south, west, north, east, ct);
        if (!r.IsOk || r.Value is not { } box) return null;
        return new ServerGymsAnswer(box.Gyms.Select(g => new FetchedGym(g.OsmType switch
        {
            OsmType.Node => OsmElementType.Node,
            OsmType.Way => OsmElementType.Way,
            _ => OsmElementType.Relation,
        }, g.OsmId, g.Name, g.Address, g.Latitude, g.Longitude)).ToList(), box.Covered);
    }
}
