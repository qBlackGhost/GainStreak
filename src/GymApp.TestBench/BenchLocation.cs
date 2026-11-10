using System;
using System.Threading.Tasks;
using GymApp.App;
using GymApp.Core;

namespace GymApp.TestBench;

/// <summary>The TestBench's stand-in for the phone's location (18c), set from the admin window: at a gym (its
/// position, 20 m accurate), away (nowhere near), approximate (precise location off) or no permission. Not kept across restarts: it starts away.</summary>
public sealed class BenchLocation : ILocationProvider
{
    public enum Mode { Away, AtGym, NoPermission, Approximate, InAnotherCity }

    /// <summary>"In another city" (19c): central Rome, far from any home city the TestBench is likely to use.</summary>
    public static readonly (double Lat, double Lon) AnotherCity = (41.9028, 12.4964);

    public Mode Current { get; private set; } = Mode.Away;
    private (double Lat, double Lon)? _gym;

    public void SetAtGym(double latitude, double longitude)
    {
        Current = Mode.AtGym;
        _gym = (latitude, longitude);
    }

    public void SetAway() => Current = Mode.Away;
    public void SetInAnotherCity() => Current = Mode.InAnotherCity;
    public void SetNoPermission() => Current = Mode.NoPermission;
    public void SetApproximate() => Current = Mode.Approximate;

    public bool IsSupported => true;
    public bool HasPermission => Current != Mode.NoPermission;
    public bool IsPrecise => Current is Mode.Away or Mode.AtGym or Mode.InAnotherCity;
    public bool NeedsNoteBeforePrompt => true;   // like Android, so the note can be tried here
    public Task<bool> RequestPermissionAsync() => Task.FromResult(HasPermission);

    // Away: the middle of the Atlantic, far from any cached gym
    public Task<LocationFix?> GetFixAsync(TimeSpan timeout) => Task.FromResult<LocationFix?>(Current switch
    {
        Mode.AtGym when _gym is { } g => new LocationFix(g.Lat, g.Lon, 20),
        Mode.Away => new LocationFix(30, -40, 20),
        Mode.InAnotherCity => new LocationFix(AnotherCity.Lat, AnotherCity.Lon, 30),
        Mode.Approximate => new LocationFix(30, -40, 1500),
        _ => null
    });

    public void OpenSettings() { }
}
