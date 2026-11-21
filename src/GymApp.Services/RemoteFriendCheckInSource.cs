using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>
/// Friends' check-ins from the server, mapped back onto this phone's own cities and gyms so the map rules work as they
/// do locally. Gyms and cities travel by OSM type and id, since local ids differ per phone:
/// <list type="bullet">
/// <item>A city with no local match gets a null <see cref="FriendCheckIn.CityId"/>, so it lands under "other cities"
/// with the name the server sent.</item>
/// <item>A gym with no active local match (a friend's custom gym, or an OSM gym this phone hasn't fetched) gets a
/// negative <see cref="FriendCheckIn.GymId"/>, unique within the result, so it can't collide with a local id. It keeps
/// the server's name and position (a custom gym has none).</item>
/// </list>
/// Distances stay null: <see cref="FriendMapService"/> computes them from the phone's own origin. The server's raw
/// answer is cached (key <see cref="SocialCacheKeys.CheckIns"/>) and mapped again on every read, so a city or gym
/// fetched later is matched in the cached rows too.
/// </summary>
public class RemoteFriendCheckInSource(
    SocialApiClient api, SocialCacheStore cache, SessionService session, IDbContextFactory<AppDbContext> dbFactory)
    : IFriendCheckInSource
{
    public async Task<IReadOnlyList<FriendCheckIn>> GetFriendGymCheckInsAsync(int userId, DateOnly since)
    {
        SignedInGuard.Check(session, userId);
        var r = await api.GetFriendCheckInsAsync(since);
        if (r.IsOk)
        {
            await cache.SaveAsync(userId, SocialCacheKeys.CheckIns, r.Value!);
            return new CachedList<FriendCheckIn>(await MapAsync(r.Value!), fetchedAt: cache.Now);
        }
        if (await cache.GetAsync<List<FriendCheckInResponse>>(userId, SocialCacheKeys.CheckIns) is { } hit)
            return new CachedList<FriendCheckIn>(await MapAsync(hit.Value.Where(c => c.Date >= since).ToList()),
                isStale: true, fetchedAt: hit.FetchedAt);
        return new CachedList<FriendCheckIn>([], isStale: true);
    }

    /// <summary>The mapping on its own, for the tests.</summary>
    public async Task<IReadOnlyList<FriendCheckIn>> MapAsync(IReadOnlyList<FriendCheckInResponse> rows)
    {
        if (rows.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync();

        var cityKeys = rows.Select(c => c.CityOsmId).Distinct().ToList();
        var cities = (await db.Cities.AsNoTracking().Where(c => cityKeys.Contains(c.OsmId)).ToListAsync())
            .ToDictionary(c => (c.OsmType, c.OsmId), c => c.Id);

        var gymKeys = rows.Where(c => c.GymOsmId is not null).Select(c => c.GymOsmId!.Value).Distinct().ToList();
        var cityIds = cities.Values.ToList();
        var gyms = (await db.Gyms.AsNoTracking()
                .Where(g => g.IsActive && g.OwnerUserId == null && g.OsmId != null && gymKeys.Contains(g.OsmId.Value) && cityIds.Contains(g.CityId))
                .ToListAsync())
            .ToDictionary(g => (g.CityId, g.OsmType!.Value, g.OsmId!.Value));

        // One negative id per gym the phone doesn't know: an OSM gym by its key, a custom gym per friend and name
        var unknownIds = new Dictionary<string, int>();
        int UnknownId(string key) => unknownIds.TryGetValue(key, out var id) ? id : unknownIds[key] = -(unknownIds.Count + 1);

        return rows.Select(c =>
        {
            int? cityId = cities.TryGetValue((ToCore(c.CityOsmType), c.CityOsmId), out var cid) ? cid : null;
            var isCustom = c.GymOsmId is null || c.GymOsmType is null;

            if (!isCustom && cityId is { } city && gyms.TryGetValue((city, ToCore(c.GymOsmType!.Value), c.GymOsmId!.Value), out var gym))
                return new FriendCheckIn(c.UserId, c.Username, gym.Id, gym.Name, cityId, c.CityName,
                    gym.Latitude ?? c.Latitude, gym.Longitude ?? c.Longitude, null, false, c.Date, c.FeaturedBadge);

            var key = isCustom
                ? $"custom:{c.UserId}:{c.CityOsmType}:{c.CityOsmId}:{c.GymName}"
                : $"osm:{c.GymOsmType}:{c.GymOsmId}";
            return new FriendCheckIn(c.UserId, c.Username, UnknownId(key), c.GymName, cityId, c.CityName,
                isCustom ? null : c.Latitude, isCustom ? null : c.Longitude, null, isCustom, c.Date, c.FeaturedBadge);
        }).ToList();
    }

    private static OsmElementType ToCore(OsmType t) => t switch
    {
        OsmType.Node => OsmElementType.Node,
        OsmType.Way => OsmElementType.Way,
        _ => OsmElementType.Relation
    };
}
