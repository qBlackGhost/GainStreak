using GymApp.Contracts;
using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server.Endpoints;

/// <summary>
/// Gyms from the server's copy of OpenStreetMap (0.27.0): <c>GET /v1/gyms?south=&amp;west=&amp;north=&amp;east=</c>, the active
/// gyms in a city's box. Signed in, at the global read limit. The app asks here first and falls back to Overpass when the box
/// isn't covered (a country not loaded, or data older than <see cref="FreshDays"/> days) or the server can't answer. Old
/// builds never call it.
/// </summary>
public static class GymEndpoints
{
    /// <summary>A box at most this many degrees each way: a city, not a country.</summary>
    public const double MaxBoxDegrees = 2;
    /// <summary>More gyms than this in one box is refused (Romania has about 550 in all).</summary>
    public const int MaxGyms = 2000;
    /// <summary>A country's data counts as covering a box for this long after it was taken; the import runs weekly.</summary>
    public const int FreshDays = 45;

    public static void MapGyms(this IEndpointRouteBuilder app) => app.MapGet("/v1/gyms", GetGyms);

    private static async Task<IResult> GetGyms(double? south, double? west, double? north, double? east, ServerDbContext db,
        TimeProvider clock)
    {
        if (south is not { } s || west is not { } w || north is not { } n || east is not { } e)
            return Problems.BadRequest("Send south, west, north and east.");
        if (!double.IsFinite(s) || !double.IsFinite(w) || !double.IsFinite(n) || !double.IsFinite(e)
            || s < -90 || n > 90 || w < -180 || e > 180 || s >= n || w >= e)
            return Problems.BadRequest("That isn't a map box: south must be below north and west left of east.");
        if (n - s > MaxBoxDegrees || e - w > MaxBoxDegrees)
            return Problems.BadRequest($"Ask for at most {MaxBoxDegrees:0} degrees each way.");

        var gyms = await db.OsmGyms.AsNoTracking()
            .Where(g => g.IsActive && g.Latitude >= s && g.Latitude <= n && g.Longitude >= w && g.Longitude <= e)
            .OrderBy(g => g.OsmType).ThenBy(g => g.OsmId)
            .Take(MaxGyms + 1)
            .Select(g => new ServerGym(g.OsmType, g.OsmId, g.Name, g.Address, g.Latitude, g.Longitude))
            .ToListAsync();
        if (gyms.Count > MaxGyms) return Problems.BadRequest("Too many gyms in that box. Ask for a smaller one.");

        var freshFrom = clock.GetUtcNow().UtcDateTime.AddDays(-FreshDays);
        var country = await db.OsmGymCountries.AsNoTracking()
            .Where(c => c.South <= s && c.North >= n && c.West <= w && c.East >= e && c.DataAsOf >= freshFrom)
            .OrderByDescending(c => c.DataAsOf)
            .FirstOrDefaultAsync();
        return Results.Ok(new GymBoxResponse(gyms, country is not null, country?.DataAsOf));
    }
}
