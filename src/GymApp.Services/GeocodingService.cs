using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Core;

namespace GymApp.Services;

/// <summary>Candidates is empty when nothing matched (Success true) or when the search failed (Error set).</summary>
public record CitySearchResult(bool Success, IReadOnlyList<CityCandidate> Candidates, string? Error = null)
{
    public static CitySearchResult Fail(string error) => new(false, [], error);
}

/// <summary>City search through Nominatim. At most one request per second, as the usage policy asks, so
/// register it as a singleton and search on button press, not per keystroke. Never throws for network trouble.</summary>
public class GeocodingService
{
    public const int MaxQueryLength = 100;
    private const string BaseUrl = "https://nominatim.openstreetmap.org/search";

    private readonly HttpClient _http;
    private readonly RequestThrottle _throttle;

    public GeocodingService() : this(null, TimeSpan.FromSeconds(1)) { }

    /// <param name="handler">For tests: answers instead of the real API.</param>
    public GeocodingService(HttpMessageHandler? handler, TimeSpan minInterval, TimeSpan? timeout = null)
    {
        _http = OsmHttp.CreateClient(timeout ?? TimeSpan.FromSeconds(10), handler);
        _throttle = new RequestThrottle(minInterval);
    }

    public async Task<CitySearchResult> SearchAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0) return CitySearchResult.Fail("Type a city name to search.");
        if (query.Length > MaxQueryLength) return CitySearchResult.Fail("That name is too long.");

        // featureType=settlement narrows it down; the parser drops the counties that still come through
        var url = $"{BaseUrl}?q={Uri.EscapeDataString(query)}&format=jsonv2&limit={Nominatim.RequestLimit}&featureType=settlement";
        try
        {
            return await _throttle.RunAsync(async () =>
            {
                using var response = await _http.GetAsync(url, ct);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    return CitySearchResult.Fail("Too many searches. Wait a moment and try again.");
                if (!response.IsSuccessStatusCode)
                    return CitySearchResult.Fail($"City search is unavailable right now (HTTP {(int)response.StatusCode}). Try again later.");

                var json = await response.Content.ReadAsStringAsync(ct);
                return new CitySearchResult(true, Nominatim.ParseSearch(json));
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return CitySearchResult.Fail("City search took too long. Try again."); }
        catch (HttpRequestException) { return CitySearchResult.Fail("Couldn't reach the city search. Check your internet connection."); }
        catch (JsonException) { return CitySearchResult.Fail("City search sent back something unexpected. Try again later."); }
    }
}
