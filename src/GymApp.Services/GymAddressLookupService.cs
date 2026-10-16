using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

public record AddressLookupProgress(int Done, int Total);

/// <summary>Looked = lookups that ran, Found = of those, how many had a street. Error is set when a lookup failed;
/// the rest stay for next time (Remaining).</summary>
public record AddressLookupResult(int Looked, int Found, int Remaining, string? Error = null);

/// <summary>Fills in an approximate address for OSM gyms that have none, from Nominatim's reverse lookup, so
/// same-named gyms ("World Class" x4) can be told apart. Each gym is looked up once; results are saved, so the picker
/// stays offline afterwards. Runs at the shared one-request-per-second limit. Register as a singleton.</summary>
public class GymAddressLookupService(IDbContextFactory<AppDbContext> dbFactory, GeocodingService geocoding,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);   // one batch at a time; a second caller finds nothing left to do

    /// <summary>How many gyms in the city still need a lookup.</summary>
    public async Task<int> CountMissingAsync(int cityId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await Missing(db, cityId).CountAsync();
    }

    public async Task<AddressLookupResult> FillMissingAsync(int cityId, IProgress<AddressLookupProgress>? progress = null,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            int[] ids;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
                ids = await Missing(db, cityId).OrderBy(g => g.Id).Select(g => g.Id).ToArrayAsync(ct);

            int looked = 0, found = 0;
            foreach (var id in ids)
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var gym = await db.Gyms.FirstOrDefaultAsync(g => g.Id == id, ct);
                if (gym is not { Latitude: { } lat, Longitude: { } lon }) continue;

                var r = await geocoding.ReverseAsync(lat, lon, ct);
                if (!r.Success)
                    return new AddressLookupResult(looked, found, ids.Length - looked, r.Error);   // try the rest next time

                gym.LookedUpAddress = r.Address;
                gym.AddressLookedUpAt = _clock.GetUtcNow().UtcDateTime;   // even when nothing was found, so it isn't retried
                await db.SaveChangesAsync(ct);   // per gym, so progress survives closing the app

                looked++;
                if (r.Address is not null) found++;
                progress?.Report(new AddressLookupProgress(looked, ids.Length));
            }
            return new AddressLookupResult(looked, found, ids.Length - looked);
        }
        finally { _gate.Release(); }
    }

    // Active OSM gyms with a position, no address from OSM, and no lookup yet. Custom gyms are never looked up.
    private static IQueryable<Data.Models.Gym> Missing(AppDbContext db, int cityId) =>
        db.Gyms.Where(g => g.CityId == cityId && g.OwnerUserId == null && g.IsActive && g.Address == null &&
                           g.AddressLookedUpAt == null && g.Latitude != null && g.Longitude != null);
}
