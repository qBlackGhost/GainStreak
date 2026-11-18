using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Services;

namespace GymApp.App;

/// <summary>Keeps the scheduled reminders (16g) equal to what <see cref="ReminderService"/> plans now: at startup, on
/// coming back to the front, on every screen change, after anything that changes the streak (the sync hooks), and on
/// sign-in or sign-out (signed out, none). Does nothing where there are no notifications (the desktop).</summary>
public sealed class ReminderSync
{
    private readonly ReminderService _reminders;
    private readonly SessionService _session;
    private readonly INotificationScheduler _scheduler;
    private readonly ErrorLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ReminderSync(ReminderService reminders, SessionService session, INotificationScheduler scheduler, ErrorLog log)
    {
        _reminders = reminders;
        _session = session;
        _scheduler = scheduler;
        _log = log;
        _session.Changed += RequestRefresh;
    }

    public void RequestRefresh()
    {
        if (_scheduler.IsSupported) _ = RefreshAsync();
    }

    /// <summary>One at a time, so the last change wins.</summary>
    public async Task RefreshAsync()
    {
        if (!_scheduler.IsSupported) return;
        await _gate.WaitAsync();
        try
        {
            IReadOnlyList<PlannedReminder> plan = _session.CurrentUser is { } user
                ? await _reminders.PlanAsync(user.Id, AppClock.Now)
                : [];
            _scheduler.Replace(plan);
        }
        catch (Exception e) { _log.Write(e, "Reminders"); }
        finally { _gate.Release(); }
    }
}

/// <summary>The sync hooks with the reminders on top: anything that changes the streak (a check-in, an undo, the
/// split) also replans them, and redraws the Home Screen widgets (Phase 24). The widgets come late
/// (<paramref name="widgets"/> is called on the first change): their snapshot reads through services that take these
/// very hooks, so resolving them up front would loop.</summary>
public sealed class ReminderHooks(ISocialSyncHooks inner, ReminderSync reminders, Func<WidgetSync>? widgets = null) : ISocialSyncHooks
{
    public void MarkStatsDirty(int userId) { inner.MarkStatsDirty(userId); reminders.RequestRefresh(); widgets?.Invoke().RequestRefresh(); }
    public void MarkCheckInsDirty(int userId) { inner.MarkCheckInsDirty(userId); reminders.RequestRefresh(); widgets?.Invoke().RequestRefresh(); }
    public void MarkSharingDirty(int userId) => inner.MarkSharingDirty(userId);
    public void MarkAvatarDirty(int userId) => inner.MarkAvatarDirty(userId);
}
