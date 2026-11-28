using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App;

/// <summary>
/// Notices a trip away from the home city (19c, docs/PHASE_19_VACATION.md): when Home appears, at most every 6 hours,
/// one fix if location is already allowed and precise. This never asks for the permission and never runs in the
/// background. The fix is compared on the phone and dropped; only "away, ask" is kept, here in memory, so the prompt
/// stays on Home until the user answers or the app restarts. The desktop has no location, so nothing happens there.
/// </summary>
public sealed class AwayWatcher(VacationService vacations, ILocationProvider location)
{
    public static readonly TimeSpan FixTimeout = TimeSpan.FromSeconds(10);

    /// <summary>After a look that got no fix, the next try waits this long (not the 6 hours of a real look).</summary>
    public static readonly TimeSpan NoFixRetry = TimeSpan.FromMinutes(15);

    /// <summary>The clock; tests set it so they don't shift <see cref="AppClock"/>.</summary>
    public Func<DateTime> Now { get; init; } = () => AppClock.Now;

    private readonly Dictionary<int, string> _pending = new();   // user → home city name, while the prompt is up
    private readonly Dictionary<int, DateTime> _noFixAt = new();  // user → when a look last got no fix
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The home city's name when Home should ask "Away from …?", or null. Looks again only when it's due.</summary>
    public async Task<string?> CheckAsync(int userId)
    {
        if (!location.IsSupported || !location.HasPermission || !location.IsPrecise) return Pending(userId);
        await _gate.WaitAsync();
        try
        {
            if (_pending.TryGetValue(userId, out var city)) return city;
            var now = Now();
            if (_noFixAt.TryGetValue(userId, out var failed) && now >= failed && now - failed < NoFixRetry) return null;
            if (await vacations.AwayCheckDueAsync(userId, now) is not { } home) return null;
            var fix = await location.GetFixAsync(FixTimeout);
            if (fix is null) { _noFixAt[userId] = now; return null; }
            _noFixAt.Remove(userId);
            if (!await vacations.RecordAwayFixAsync(userId, home, fix, now)) return null;
            _pending[userId] = home.CityName;
            return home.CityName;
        }
        finally { _gate.Release(); }
    }

    /// <summary>The prompt already up for the user, without looking again.</summary>
    public string? Pending(int userId) => _pending.GetValueOrDefault(userId);

    /// <summary>"Not on vacation": stored, so this trip doesn't ask again.</summary>
    public async Task DismissAsync(int userId)
    {
        _pending.Remove(userId);
        await vacations.DismissAwayPromptAsync(userId);
    }

    /// <summary>"Not now" (a missed day to settle first), or a vacation started: the prompt goes, nothing is stored.</summary>
    public void Clear(int userId) => _pending.Remove(userId);
}
