using System;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Data.Models;
using GymApp.Services;

namespace GymApp.App;

/// <param name="City">The city being visited, now the map's city until the vacation ends; null when it failed.</param>
/// <param name="Error">Why there's no city, for the map: "Couldn't find where you are" and the like.</param>
/// <param name="GymsError">The city was found but its gyms couldn't be loaded (they can be refreshed from the picker).</param>
public record NearbyCityResult(City? City, string? Error = null, string? GymsError = null);

/// <summary>
/// "Show me gyms near me" (19d, docs/PHASE_19_VACATION.md): one fix, the city at that point from Nominatim (only a
/// position rounded to about 1 km is sent, and only because the user tapped), the city saved as the vacation's visiting
/// city, and its gyms loaded from OpenStreetMap. Live workouts there are then verified as at any cached gym.
/// </summary>
public sealed class NearbyCityFinder(ILocationProvider location, GeocodingService geocoding, CityService cities,
    GymFetchService fetch, VacationService vacations)
{
    public const string NoFixText = "Couldn't find where you are.";

    public bool IsSupported => location.IsSupported;

    public async Task<NearbyCityResult> FindAsync(int userId, CancellationToken ct = default)
    {
        if (!location.IsSupported) return new(null, "Location isn't available here.");
        if (!location.HasPermission && !await location.RequestPermissionAsync())
            return new(null, "Location is off for GainStreak. Allow it in Settings, or search for the city instead.");

        if (await location.GetFixAsync(AwayWatcher.FixTimeout) is not { } fix) return new(null, NoFixText);
        var r = await geocoding.ReverseCityAsync(fix.Latitude, fix.Longitude, ct);
        if (!r.Success) return new(null, r.Error ?? NoFixText);
        if (r.City is not { } candidate) return new(null, "You don't seem to be in a city. Search for the nearest one instead.");

        var city = await cities.UpsertAsync(candidate);
        await vacations.SetVisitingCityAsync(userId, city.Id);
        var gyms = await fetch.RefreshIfNeededAsync(city.Id, ct);
        return new(city, GymsError: gyms.Error);
    }
}
