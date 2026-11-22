using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GymApp.Services;

/// <summary>Shared setup for the OpenStreetMap APIs (Nominatim, Overpass) and tiles: they ask for an identifying
/// User-Agent. Forwards to <see cref="AppHttp"/>, which Open Food Facts uses too.</summary>
public static class OsmHttp
{
    public static string UserAgent { get => AppHttp.UserAgent; set => AppHttp.UserAgent = value; }

    public static string ForPlatform(string version, string platform) => AppHttp.ForPlatform(version, platform);

    public static HttpClient CreateClient(TimeSpan timeout, HttpMessageHandler? handler = null) =>
        AppHttp.CreateClient(timeout, handler);
}

/// <summary>Lets one request through at a time, at least <c>minInterval</c> apart. Keep one per API.</summary>
public sealed class RequestThrottle(TimeSpan minInterval)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastStart;   // Stopwatch timestamp; 0 = nothing sent yet

    public async Task<T> RunAsync<T>(Func<Task<T>> send, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_lastStart != 0)
            {
                var wait = minInterval - Stopwatch.GetElapsedTime(_lastStart);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            }
            _lastStart = Stopwatch.GetTimestamp();
            return await send();
        }
        finally { _gate.Release(); }
    }
}
