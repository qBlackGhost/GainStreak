using System;

namespace GymApp.Core;

public static class GymCache
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <summary>Fetch when the city was never fetched, or the last successful fetch is older than 30 days.</summary>
    public static bool IsStale(DateTime? fetchedAtUtc, DateTime nowUtc) =>
        fetchedAtUtc is not { } at || nowUtc - at > MaxAge;
}
