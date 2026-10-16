using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Data.Models;

namespace GymApp.Services;

/// <summary>Candidates is empty when nothing matched (Success true) or when the search failed (Error set).</summary>
public record CitySearchResult(bool Success, IReadOnlyList<CityCandidate> Candidates, string? Error = null)
{
    public static CitySearchResult Fail(string error) => new(false, [], error);
}

/// <summary>Street addresses inside one city. Same shape as CitySearchResult.</summary>
public record AddressSearchResult(bool Success, IReadOnlyList<AddressCandidate> Candidates, string? Error = null)
{
    public static AddressSearchResult Fail(string error) => new(false, [], error);
}

/// <summary>Success with a null Address means Nominatim had no street there; the lookup still counts as done.</summary>
public record ReverseResult(bool Success, string? Address = null, string? Error = null);

/// <summary>City search, address search and reverse lookups through Nominatim. All share one limit of one request
/// per second, as the usage policy asks, so register it as a singleton. Searches run on button press, never per
/// keystroke (the policy forbids autocomplete). Never throws for network trouble.</summary>
public class GeocodingService
{
    public const int MaxQueryLength = 100;
    private const string BaseUrl = "https://nominatim.openstreetmap.org";

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
        var url = $"{BaseUrl}/search?q={Uri.EscapeDataString(query)}&format=jsonv2&limit={Nominatim.RequestLimit}&featureType=settlement";
        var (list, error) = await GetAsync(url, Nominatim.ParseSearch, "City search", ct);
        return list is null ? CitySearchResult.Fail(error!) : new CitySearchResult(true, list);
    }

    /// <summary>Street addresses inside the city's box only (viewbox + bounded), for custom gyms.</summary>
    public async Task<AddressSearchResult> SearchAddressAsync(string query, City city, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0) return AddressSearchResult.Fail("Type a street, e.g. Calea Aradului 1.");
        if (query.Length > MaxQueryLength) return AddressSearchResult.Fail("That address is too long.");

        // viewbox is left,top,right,bottom
        var box = string.Create(CultureInfo.InvariantCulture, $"{city.West},{city.North},{city.East},{city.South}");
        var url = $"{BaseUrl}/search?q={Uri.EscapeDataString(query)}&format=jsonv2&addressdetails=1" +
                  $"&limit={Addresses.RequestLimit}&viewbox={box}&bounded=1";
        var (list, error) = await GetAsync(url, Addresses.ParseSearch, "Address search", ct);
        return list is null ? AddressSearchResult.Fail(error!) : new AddressSearchResult(true, list);
    }

    /// <summary>The nearest street address to a point (zoom=18 = building level).</summary>
    public async Task<ReverseResult> ReverseAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"{BaseUrl}/reverse?lat={latitude}&lon={longitude}&format=jsonv2&zoom=18&addressdetails=1");
        var (address, error) = await GetAsync(url, json => new Box<string?>(Addresses.ParseReverse(json)), "Address lookup", ct);
        return address is null ? new ReverseResult(false, Error: error) : new ReverseResult(true, address.Value);
    }

    private sealed record Box<T>(T Value);   // so "found nothing" (null inside) differs from "failed" (null box)

    /// <summary>One throttled GET. Value null = failed, with a message the UI can show.</summary>
    private async Task<(T? Value, string? Error)> GetAsync<T>(string url, Func<string, T> parse, string what, CancellationToken ct)
        where T : class
    {
        try
        {
            return await _throttle.RunAsync(async () =>
            {
                using var response = await _http.GetAsync(url, ct);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    return ((T?)null, (string?)"Too many requests. Wait a moment and try again.");
                if (!response.IsSuccessStatusCode)
                    return (null, $"{what} is unavailable right now (HTTP {(int)response.StatusCode}). Try again later.");

                var json = await response.Content.ReadAsStringAsync(ct);
                return (parse(json), null);
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return (null, $"{what} took too long. Try again."); }
        catch (HttpRequestException) { return (null, $"Couldn't reach the {what.ToLowerInvariant()}. Check your internet connection."); }
        catch (JsonException) { return (null, $"{what} sent back something unexpected. Try again later."); }
    }
}
