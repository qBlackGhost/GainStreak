using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>Cities are shared rows, one per OSM object, created when someone picks a search result.</summary>
public class CityService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>Finds the city by OSM type and id, or adds it. Name, centre and box are refreshed from the
    /// candidate; GymsFetchedAt is kept, so the gym cache survives picking the same city again.</summary>
    public async Task<City> UpsertAsync(CityCandidate c)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var city = await db.Cities.FirstOrDefaultAsync(x => x.OsmType == c.OsmType && x.OsmId == c.OsmId);
        if (city is null)
        {
            city = new City { OsmType = c.OsmType, OsmId = c.OsmId };
            db.Cities.Add(city);
        }

        city.Name = c.Name;
        city.DisplayName = c.DisplayName;
        city.CenterLatitude = c.CenterLatitude;
        city.CenterLongitude = c.CenterLongitude;
        city.South = c.South;
        city.West = c.West;
        city.North = c.North;
        city.East = c.East;

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // Saved by someone else in the meantime: the unique index caught it, so read theirs
            await using var again = await dbFactory.CreateDbContextAsync();
            return await again.Cities.AsNoTracking().FirstAsync(x => x.OsmType == c.OsmType && x.OsmId == c.OsmId);
        }
        return city;
    }

    public async Task<City?> GetAsync(int cityId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cityId);
    }
}
