using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App;

/// <summary>Whether the signed-in account turned Apple Health (Phase 20) or Health Connect (Phase 23) on.</summary>
public interface IHealthSetting
{
    Task<bool> IsEnabledAsync(int userId);
}

/// <summary>The Profile switch, <c>UserProfile.HealthEnabled</c>.</summary>
public sealed class ProfileHealthSetting(ProfileService profiles) : IHealthSetting
{
    public Task<bool> IsEnabledAsync(int userId) => profiles.IsHealthEnabledAsync(userId);
}

/// <summary>What one read from Health did, for the Apple Health card.</summary>
public record HealthReadResult(int WeighInsImported, bool ProfileFilled, HealthProfileOffer? Offer,
    ActivityLevel? StepsSuggestion, double? AverageSteps);

/// <summary>
/// Phase 20: keeps Apple Health in step with the app for the last <see cref="WindowDays"/> days. Each run builds what
/// Health should hold (<see cref="HealthExportService"/>), reads what this account already wrote there, and saves or
/// deletes the difference (<see cref="HealthPlan.Diff"/>), only for the kinds the user lets the app write. Nothing is
/// kept locally, so Undo, a deleted weigh-in and a failed write are all fixed by the next run. Runs when the app comes
/// to the front or goes to the back (a finished workout, cardio or weigh-in is in by then), one run at a time; a
/// request during a run starts one more afterwards. Does nothing with Health off, signed out, or on a head without
/// Health. Errors are logged without any Health values (decision 2).
/// <para>20c: each run also reads from Health (<see cref="ReadAsync"/>), at most once an hour; Profile reads at once.</para>
/// </summary>
public sealed class HealthSync(IHealthStore store, HealthExportService export, SessionService session,
    IHealthSetting setting, ErrorLog log, TimeProvider? clock = null, HealthImportService? import = null)
{
    public const int WindowDays = 30;

    /// <summary>The least time between two reads started by the app coming and going.</summary>
    public static readonly TimeSpan ReadEvery = TimeSpan.FromHours(1);

    private DateTime? _lastRead;

    /// <summary>A background run finished (any thread): imported weigh-ins may have earned a badge (Phase 25).</summary>
    public event Action? Ran;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pending;

    private DateTime Now => clock is null ? AppClock.Now : clock.GetLocalNow().DateTime;

    /// <summary>Starts a run in the background, or one more after the current one.</summary>
    public void RequestSync()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync();
            try
            {
                Interlocked.Exchange(ref _pending, 0);
                await SyncAsync();
                await ReadAsync(force: false);
            }
            finally { _gate.Release(); }
            Ran?.Invoke();
        });
    }

    /// <summary>One run. Returns what it changed, for tests; null when it didn't run.</summary>
    public async Task<HealthChanges?> SyncAsync()
    {
        if (!store.IsSupported || store.NeedsInstall || session.CurrentUser is not { } user) return null;
        try
        {
            if (!await setting.IsEnabledAsync(user.Id)) return null;
            var now = Now;
            var today = DateOnly.FromDateTime(now);
            var from = today.AddDays(1 - WindowDays);
            var wanted = await export.WantedAsync(user.Id, from, today);
            var existing = await store.ReadOwnAsync(from.ToDateTime(TimeOnly.MinValue), today.AddDays(1).ToDateTime(TimeOnly.MinValue));
            var changes = HealthPlan.Diff(user.Id, wanted, existing, now);
            var failed = 0;
            foreach (var item in changes.Delete.Where(Allowed))
            {
                try { await store.DeleteAsync(item); }
                catch (Exception) { failed++; }
            }
            foreach (var item in changes.Save.Where(Allowed))
            {
                try { await store.SaveAsync(item); }
                catch (Exception) { failed++; }
            }
            // The count only: never the values (decision 2)
            if (failed > 0) log.Write(new InvalidOperationException($"{failed} of {changes.Save.Count + changes.Delete.Count} Health changes failed"), "Apple Health (sync)");
            return changes;
        }
        catch (Exception e)
        {
            log.Write(new InvalidOperationException($"Health sync failed: {e.GetType().Name}"), "Apple Health (sync)");
            return null;
        }
    }

    private bool Allowed(HealthItem item) => store.WriteAccess(item.Kind) == HealthAccess.Allowed;

    /// <summary>
    /// 20c: imports new weigh-ins, fills empty profile fields, and works out what to offer (differing profile values,
    /// an activity level from the last 30 days of steps). Without <paramref name="force"/>, at most once an hour.
    /// Null when it didn't run.
    /// </summary>
    public async Task<HealthReadResult?> ReadAsync(bool force)
    {
        if (import is null || !store.IsSupported || store.NeedsInstall || session.CurrentUser is not { } user) return null;
        var now = Now;
        if (!force && _lastRead is { } last && now - last < ReadEvery) return null;
        try
        {
            if (!await setting.IsEnabledAsync(user.Id)) return null;
            _lastRead = now;
            var today = DateOnly.FromDateTime(now);

            // Each read on its own (0.27.1): what the user shares comes in even when another type fails or isn't shared
            var imported = await Part("weigh-ins", 0, async () =>
                await import.WeightImportStartAsync(user.Id) is var (from, anchor)
                    ? await import.ImportWeighInsAsync(user.Id, await store.ReadWeightsAsync(from, anchor), today)
                    : 0);

            var profile = await Part("profile", ((bool Filled, HealthProfileOffer? Offer)?)null, async () =>
                await import.ApplyProfileAsync(user.Id, await store.ReadProfileAsync(), today));

            var steps = await Part("steps", (double?)null, async () =>
                HealthImport.AverageSteps(await store.ReadDailyStepsAsync(today.AddDays(-HealthImport.StepDays), today)));
            var suggestion = await Part("steps", (ActivityLevel?)null, () => import.StepsSuggestionAsync(user.Id, steps));
            return new HealthReadResult(imported, profile?.Filled == true, profile?.Offer, suggestion, steps);
        }
        catch (Exception e)
        {
            log.Write(new InvalidOperationException($"Health read failed: {e.GetType().Name}"), "Apple Health (read)");
            return null;
        }
    }

    /// <summary>One part of <see cref="ReadAsync"/>: a failure is logged and gives <paramref name="none"/>, so the
    /// other parts still run.</summary>
    private async Task<T> Part<T>(string what, T none, Func<Task<T>> read)
    {
        try { return await read(); }
        catch (Exception e)
        {
            log.Write(new InvalidOperationException($"Health read ({what}) failed: {e.GetType().Name}"), "Apple Health (read)");
            return none;
        }
    }
}
