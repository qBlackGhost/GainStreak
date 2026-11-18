using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App;

/// <summary>
/// The iPhone's Home Screen widgets and Control Center controls (Phase 24): the head saves the snapshot where its
/// widget extension reads it and asks the system to redraw them. Other heads have none (<see cref="NoHomeWidgets"/>).
/// Calls never throw.
/// </summary>
public interface IHomeWidgets
{
    bool IsSupported { get; }

    /// <summary>Saves <paramref name="json"/> (a <see cref="WidgetSnapshot"/>) for the widgets and redraws them when it
    /// changed.</summary>
    void Save(string json);
}

public sealed class NoHomeWidgets : IHomeWidgets
{
    public bool IsSupported => false;
    public void Save(string json) { }
}

/// <summary>
/// Keeps the widgets' snapshot current (Phase 24): at startup, on every screen change, on going to the background,
/// after anything that changes the streak or points (the sync hooks), and on sign-in or sign-out (signed out, the
/// widgets show no one's data). Does nothing where there are no widgets.
/// </summary>
public sealed class WidgetSync
{
    private readonly WidgetSnapshotService _snapshots;
    private readonly SessionService _session;
    private readonly IHomeWidgets _widgets;
    private readonly IHealthStore _health;
    private readonly IHealthSetting _healthSetting;
    private readonly ErrorLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pending;

    public WidgetSync(WidgetSnapshotService snapshots, SessionService session, IHomeWidgets widgets, IHealthStore health,
        IHealthSetting healthSetting, ErrorLog log)
    {
        _snapshots = snapshots;
        _session = session;
        _widgets = widgets;
        _health = health;
        _healthSetting = healthSetting;
        _log = log;
        _session.Changed += RequestRefresh;
    }

    public void RequestRefresh()
    {
        if (_widgets.IsSupported) _ = RefreshAsync();
    }

    /// <summary>One at a time; requests that pile up while one runs become a single run after it.</summary>
    public async Task RefreshAsync()
    {
        if (!_widgets.IsSupported) return;
        if (Interlocked.Increment(ref _pending) > 2) { Interlocked.Decrement(ref _pending); return; }
        await _gate.WaitAsync();
        try
        {
            var today = AppClock.Today;
            var snapshot = _session.CurrentUser is { } user
                ? await _snapshots.BuildAsync(user.Id, today, await StepsAsync(user.Id, today))
                : WidgetSnapshot.SignedOut(today);
            _widgets.Save(snapshot.ToJson());
        }
        catch (Exception e) { _log.Write(e, "Home Screen widgets"); }
        finally
        {
            Interlocked.Decrement(ref _pending);
            _gate.Release();
        }
    }

    /// <summary>The last 7 days' steps, today last; null when Health is off or has no steps at all (a refused read
    /// looks like no data). One query per day: the store skips days without samples.</summary>
    private async Task<IReadOnlyList<double>?> StepsAsync(int userId, DateOnly today)
    {
        if (!_health.IsSupported || _health.NeedsInstall) return null;
        try
        {
            if (!await _healthSetting.IsEnabledAsync(userId)) return null;
            var days = new List<double>();
            for (var d = today.AddDays(-6); d <= today; d = d.AddDays(1))
                days.Add((await _health.ReadDailyStepsAsync(d, d.AddDays(1))).Sum());
            return days.Any(s => s > 0) ? days : null;
        }
        catch (Exception e)
        {
            _log.Write(new InvalidOperationException($"Steps for widgets failed: {e.GetType().Name}"), "Home Screen widgets");
            return null;
        }
    }
}
