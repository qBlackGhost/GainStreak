using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GymApp.Core;

namespace GymApp.App;

/// <summary>Whether the user let GainStreak write one kind of object to Health. Read access is never known (iOS hides
/// it), so there's nothing like this for reads.</summary>
public enum HealthAccess { NotAsked, Denied, Allowed }

/// <summary>
/// Apple Health (Phase 20), from the head: HealthKit on iOS (<c>IosHealthStore</c>); every other head has none
/// (<see cref="NoHealthStore"/>). Plain records in and out (<see cref="HealthItem"/>), so nothing above the head sees
/// HealthKit types. Writes throw on failure; <see cref="HealthSync"/> logs it (without values) and tries again later.
/// </summary>
public interface IHealthStore
{
    /// <summary>False on heads without Health, and on an iPhone build without the HealthKit entitlement.</summary>
    bool IsSupported { get; }

    /// <summary>Shows iOS's permission sheet (only the first time; afterwards it returns at once). False when the sheet
    /// couldn't be shown; true says nothing about what the user allowed (see <see cref="WriteAccess"/>).</summary>
    Task<bool> RequestAuthorizationAsync();

    HealthAccess WriteAccess(HealthKind kind);

    /// <summary>The objects this app wrote to Health that start in [<paramref name="from"/>, <paramref name="to"/>), with
    /// their sync ids and versions. Other apps' objects are never returned.</summary>
    Task<IReadOnlyList<HealthItem>> ReadOwnAsync(DateTime from, DateTime to);

    /// <summary>Saves <paramref name="item"/> at its <see cref="HealthItem.Version"/>, replacing the object with the same
    /// sync id.</summary>
    Task SaveAsync(HealthItem item);

    /// <summary>Deletes the app's object with <paramref name="item"/>'s sync id.</summary>
    Task DeleteAsync(HealthItem item);

    /// <summary>Opens GainStreak's page in the phone's Settings, where its Health access can be changed.</summary>
    void OpenSettings();

    // ===== Reads (20c). A refused read looks exactly like no data: empty results, never an error =====

    /// <summary>The latest height, the birth date and the sex in Health.</summary>
    Task<HealthProfile> ReadProfileAsync();

    /// <summary>Weigh-ins added since <paramref name="anchor"/> (all of them from <paramref name="from"/> when it's
    /// null), with the anchor for next time.</summary>
    Task<HealthWeights> ReadWeightsAsync(DateTime from, byte[]? anchor);

    /// <summary>Each day's total steps from <paramref name="from"/> up to but not including <paramref name="to"/>,
    /// counted once across the iPhone and a Watch.</summary>
    Task<IReadOnlyList<double>> ReadDailyStepsAsync(DateOnly from, DateOnly to);
}

public sealed class NoHealthStore : IHealthStore
{
    public bool IsSupported => false;
    public Task<bool> RequestAuthorizationAsync() => Task.FromResult(false);
    public HealthAccess WriteAccess(HealthKind kind) => HealthAccess.NotAsked;
    public Task<IReadOnlyList<HealthItem>> ReadOwnAsync(DateTime from, DateTime to) => Task.FromResult<IReadOnlyList<HealthItem>>([]);
    public Task SaveAsync(HealthItem item) => Task.CompletedTask;
    public Task DeleteAsync(HealthItem item) => Task.CompletedTask;
    public void OpenSettings() { }
    public Task<HealthProfile> ReadProfileAsync() => Task.FromResult(new HealthProfile(null, null, null));
    public Task<HealthWeights> ReadWeightsAsync(DateTime from, byte[]? anchor) => Task.FromResult(new HealthWeights([], anchor));
    public Task<IReadOnlyList<double>> ReadDailyStepsAsync(DateOnly from, DateOnly to) => Task.FromResult<IReadOnlyList<double>>([]);
}
