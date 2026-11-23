using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

public enum GymRefreshOutcome
{
    UpToDate,          // the cache is fresh; nothing was fetched
    Fetched,           // fetched and merged; GymsFetchedAt moved
    FailedWithCache,   // the fetch failed, the cached gyms are still there: show them with "Couldn't refresh"
    Failed             // the fetch failed and there is no cache yet: show the error and Retry
}

/// <summary>What a refresh did. FetchedAt is the city's last successful fetch (null = never), for "last updated".</summary>
public record GymRefreshResult(GymRefreshOutcome Outcome, DateTime? FetchedAt, string? Error = null,
    int Added = 0, int Updated = 0, int Deactivated = 0)
{
    public bool Changed => Added + Updated + Deactivated > 0;
}

/// <summary>Loads a city's gyms from Overpass into the database. The picker always reads the database; call
/// RefreshIfNeededAsync when it opens (fetches only on first use or after 30 days) and RefreshAsync for the
/// Refresh button. Register as a singleton: it spaces requests out and never runs two refreshes at once.</summary>
public class GymFetchService
{
    // The main server first; the mirror only when the main one is down or overloaded (5xx, 429, no connection).
    // Mirrors checked 2026-10-15: kumi.systems answered 500 to everything, private.coffee had 3-month-old data.
    public static readonly IReadOnlyList<string> DefaultEndpoints =
    [
        "https://overpass-api.de/api/interpreter",
        "https://maps.mail.ru/osm/tools/overpass/api/interpreter"
    ];

    /// <summary>Both servers are tried this many times, with RetryDelay between rounds, before giving up.
    /// The public servers often fail for a moment under load and work again seconds later.</summary>
    public const int Rounds = 2;
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(5);

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly HttpClient _http;
    private readonly IReadOnlyList<string> _endpoints;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _retryDelay;
    private readonly RequestThrottle _throttle;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly IServerGyms? _server;

    /// <param name="server">Our own server's gyms (0.27.0), asked first; null (the TestBench's local mode) asks Overpass only.</param>
    public GymFetchService(IDbContextFactory<AppDbContext> dbFactory, IServerGyms? server = null)
        : this(dbFactory, null, DefaultEndpoints, TimeSpan.FromSeconds(1), null, null, DefaultRetryDelay, server) { }

    /// <param name="handler">For tests: answers instead of the real API.</param>
    public GymFetchService(IDbContextFactory<AppDbContext> dbFactory, HttpMessageHandler? handler,
        IReadOnlyList<string> endpoints, TimeSpan minInterval, TimeSpan? timeout, TimeProvider? clock,
        TimeSpan retryDelay, IServerGyms? server = null)
    {
        _dbFactory = dbFactory;
        _server = server;
        // Per server. A busy server that hasn't answered in 20 s usually won't soon; the other one often answers at once
        _http = OsmHttp.CreateClient(timeout ?? TimeSpan.FromSeconds(20), handler);
        _endpoints = endpoints;
        _retryDelay = retryDelay;
        _throttle = new RequestThrottle(minInterval);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Fetches only when the city has never been fetched or the last fetch is older than 30 days.</summary>
    public Task<GymRefreshResult> RefreshIfNeededAsync(int cityId, CancellationToken ct = default) =>
        RefreshCoreAsync(cityId, force: false, ct);

    /// <summary>The Refresh button: always fetches.</summary>
    public Task<GymRefreshResult> RefreshAsync(int cityId, CancellationToken ct = default) =>
        RefreshCoreAsync(cityId, force: true, ct);

    private async Task<GymRefreshResult> RefreshCoreAsync(int cityId, bool force, CancellationToken ct)
    {
        await _refreshGate.WaitAsync(ct);
        try
        {
            City? city;
            await using (var db = await _dbFactory.CreateDbContextAsync(ct))
                city = await db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cityId, ct);
            if (city is null) return new(GymRefreshOutcome.Failed, null, "That city is no longer available.");

            // Checked inside the gate, so a second caller waiting here sees the first caller's fetch
            if (!force && !GymCache.IsStale(city.GymsFetchedAt, _clock.GetUtcNow().UtcDateTime))
                return new(GymRefreshOutcome.UpToDate, city.GymsFetchedAt);

            var (gyms, error) = await FetchAsync(city, ct);
            if (gyms is null)
                return city.GymsFetchedAt is null
                    ? new(GymRefreshOutcome.Failed, null, error)
                    : new(GymRefreshOutcome.FailedWithCache, city.GymsFetchedAt, error);

            return await SaveAsync(cityId, gyms, city.GymsFetchedAt, ct);
        }
        finally { _refreshGate.Release(); }
    }

    /// <summary>Applies the merge plan and sets GymsFetchedAt, in one transaction. Only the city's OSM gyms are
    /// loaded, so custom gyms are never touched.</summary>
    private async Task<GymRefreshResult> SaveAsync(int cityId, IReadOnlyList<FetchedGym> fetched, DateTime? previousFetch,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var city = await db.Cities.FirstAsync(c => c.Id == cityId, ct);
        var stored = await db.Gyms
            .Where(g => g.CityId == cityId && g.OwnerUserId == null && g.OsmType != null && g.OsmId != null)
            .ToListAsync(ct);

        var plan = GymMerge.Plan(
            stored.Select(g => new StoredOsmGym(g.Id, g.OsmType!.Value, g.OsmId!.Value, g.Name, g.Address,
                g.Latitude, g.Longitude, g.IsActive)),
            fetched);

        var byId = stored.ToDictionary(g => g.Id);
        foreach (var f in plan.ToAdd)
            db.Gyms.Add(new Gym
            {
                CityId = cityId, OsmType = f.OsmType, OsmId = f.OsmId, Name = f.Name, Address = f.Address,
                Latitude = f.Latitude, Longitude = f.Longitude, IsActive = true
            });
        foreach (var u in plan.ToUpdate)
        {
            var g = byId[u.Id];
            if (g.Latitude != u.Data.Latitude || g.Longitude != u.Data.Longitude)
            {
                // The gym moved, so the approximate address may be wrong: look it up again
                g.LookedUpAddress = null;
                g.AddressLookedUpAt = null;
            }
            g.Name = u.Data.Name;
            g.Address = u.Data.Address;
            g.Latitude = u.Data.Latitude;
            g.Longitude = u.Data.Longitude;
            g.IsActive = true;
        }
        foreach (var id in plan.ToDeactivate) byId[id].IsActive = false;

        city.GymsFetchedAt = _clock.GetUtcNow().UtcDateTime;   // only after a successful fetch

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return new(previousFetch is null ? GymRefreshOutcome.Failed : GymRefreshOutcome.FailedWithCache,
                previousFetch, "Couldn't save the gyms. Try again.");
        }
        return new(GymRefreshOutcome.Fetched, city.GymsFetchedAt, null, plan.ToAdd.Count, plan.ToUpdate.Count, plan.ToDeactivate.Count);
    }

    /// <summary>Gyms, or null and an error the UI can show. Never throws for network trouble.</summary>
    private async Task<(IReadOnlyList<FetchedGym>? Gyms, string? Error)> FetchAsync(City city, CancellationToken ct)
    {
        // Our server first (0.27.0): fast and not overloaded. An empty answer counts only when the server covers the box;
        // otherwise (a country it hasn't loaded, offline, refused) the public Overpass servers as before
        if (_server is not null
            && await _server.GetAsync(city.South, city.West, city.North, city.East, ct) is { } answer
            && (answer.Gyms.Count > 0 || answer.Covered))
            return (answer.Gyms, null);

        var query = Overpass.GymQuery(city.South, city.West, city.North, city.East);
        string error = "Couldn't load gyms.";

        for (var round = 0; round < Rounds; round++)
        {
            if (round > 0) await Task.Delay(_retryDelay, ct);
            foreach (var endpoint in _endpoints)
            {
                try
                {
                    var (gyms, err, retry) = await _throttle.RunAsync(() => SendAsync(endpoint, query, ct), ct);
                    if (gyms is not null) return (gyms, null);
                    error = err!;
                    if (!retry) return (null, error);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (OperationCanceledException)
                {
                    // This server sat on the request: move on to the other one instead of giving up
                    error = "Loading gyms took too long. The map server may be busy, try again later.";
                }
                catch (HttpRequestException)
                {
                    error = "Couldn't reach the map server. Check your internet connection.";
                }
            }
        }
        return (null, error);
    }

    /// <summary>Retry = worth trying again (server busy or broken answer); false for a request the server rejected.</summary>
    private async Task<(IReadOnlyList<FetchedGym>? Gyms, string? Error, bool Retry)> SendAsync(
        string endpoint, string query, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]);
        using var response = await _http.PostAsync(endpoint, content, ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return (null, "The map server is busy. Wait a minute and try again.", true);
        if ((int)response.StatusCode >= 500)
            return (null, $"The map server is unavailable right now (HTTP {(int)response.StatusCode}). Try again later.", true);
        if (!response.IsSuccessStatusCode)
            return (null, $"Couldn't load gyms (HTTP {(int)response.StatusCode}).", false);

        var json = await response.Content.ReadAsStringAsync(ct);
        try { return (Overpass.ParseGyms(json), null, false); }
        catch (OverpassRemarkException) { return (null, "The map server couldn't finish the search. Try again later.", true); }
        catch (JsonException) { return (null, "The map server sent back something unexpected. Try again later.", true); }
    }
}
