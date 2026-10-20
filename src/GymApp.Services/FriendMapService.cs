using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Social;

namespace GymApp.Services;

/// <summary>The friends layer and the Friends card's groups, for one range, mode and sort. Stale when the check-ins
/// came from the cache (the server couldn't be reached).</summary>
public record FriendMapResult(FriendMapLayout Layout, IReadOnlyList<FriendGroup> Groups) : ICachedResult
{
    public bool IsStale { get; init; }
    public DateTime? FetchedAt { get; init; }
}

/// <summary>Reads friends' shared check-ins (last 30 days) and applies <see cref="FriendMapRules"/>. Only reads; the
/// rules decide what becomes a pin. Switching the Friends card's sort needs no new read: call
/// <see cref="FriendMapRules.GroupFriends"/> again on the layout's <see cref="FriendMapLayout.LatestPerFriend"/>.</summary>
public class FriendMapService(IFriendCheckInSource source)
{
    /// <param name="origin">Where distances are measured from: the home gym's position if it has one, else the home
    /// city's centre. Computed here, on the client, so nobody's location is sent anywhere for it.</param>
    public async Task<FriendMapResult> GetAsync(
        int userId, int? homeCityId, int? homeGymId, (double Lat, double Lon)? origin,
        MapRange range, MapMode mode, FriendSort sort, DateOnly? today = null)
    {
        var day = today ?? AppClock.Today;
        var raw = await source.GetFriendGymCheckInsAsync(userId, FriendMapRules.WindowStart(day));

        var withDistance = origin is { } o
            ? raw.Select(c => c.Latitude is { } lat && c.Longitude is { } lon
                ? c with { DistanceKm = FriendMapRules.DistanceKm(o.Lat, o.Lon, lat, lon) }
                : c).ToList()
            : raw.ToList();

        var layout = FriendMapRules.Layout(withDistance, homeCityId, day, range, mode);
        var groups = FriendMapRules.GroupFriends(layout.LatestPerFriend, sort, homeCityId, homeGymId, day);
        var cached = raw as ICachedResult;
        return new FriendMapResult(layout, groups) { IsStale = cached?.IsStale ?? false, FetchedAt = cached?.FetchedAt };
    }
}
