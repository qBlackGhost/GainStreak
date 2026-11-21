using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Core;
using GymApp.Services;
using GymApp.Social;

namespace GymApp.App;

/// <summary>
/// Keeps the stored badges current (Phase 25): on sign-in, on coming back to the front, on every screen change, after
/// anything logged (the sync hooks) and after an Apple Health run. Runs one check at a time; requests that pile up
/// while one runs become a single run after it. New unlocks go out through <see cref="Unlocked"/> for the unlock sheet,
/// and a first check that found badges in the history raises <see cref="HistoryFound"/>. Never throws.
/// </summary>
public sealed class BadgeSync
{
    private readonly BadgeService _badges;
    private readonly SessionService _session;
    private readonly IFriendCheckInSource _friends;
    private readonly ErrorLog _log;
    private readonly IBadgeShareService? _share;
    private readonly ILeaderboardService? _leaderboard;
    private readonly SocialSync? _sync;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pending;
    private int _withFriends;

    /// <summary>New unlocks, newest first (any thread).</summary>
    public event Action<IReadOnlyList<BadgeUnlock>>? Unlocked;

    /// <summary>The first check stored badges from the history (any thread).</summary>
    public event Action? HistoryFound;

    public BadgeSync(BadgeService badges, SessionService session, IFriendCheckInSource friends, ErrorLog log, HealthSync? health = null,
        IBadgeShareService? share = null, ILeaderboardService? leaderboard = null, SocialSync? sync = null)
    {
        _badges = badges;
        _session = session;
        _friends = friends;
        _log = log;
        _share = share;
        _leaderboard = leaderboard;
        _sync = sync;
        _session.Changed += () => RequestRefresh(withFriends: true);
        if (health is not null) health.Ran += () => RequestRefresh();
    }

    /// <summary>Starts a check in the background. <paramref name="withFriends"/> also fetches friends' check-ins (for
    /// We Go Jim): on sign-in and on coming back to the front, not on every screen change.</summary>
    public void RequestRefresh(bool withFriends = false)
    {
        if (withFriends) Interlocked.Exchange(ref _withFriends, 1);
        _ = RefreshAsync();
    }

    /// <summary>The badges or their settings (featured, sharing) changed: they go up with the next stats upload (25d).</summary>
    public void MarkChanged()
    {
        if (_session.CurrentUser is { } user) _sync?.MarkStatsDirty(user.Id);
    }

    public async Task RefreshAsync()
    {
        if (Interlocked.Increment(ref _pending) > 2) { Interlocked.Decrement(ref _pending); return; }
        await _gate.WaitAsync();
        try
        {
            if (_session.CurrentUser is not { } user) return;
            var today = AppClock.Today;
            IReadOnlyList<FriendCheckIn>? friends = null;
            if (Interlocked.Exchange(ref _withFriends, 0) == 1)
            {
                try { friends = await _friends.GetFriendGymCheckInsAsync(user.Id, today.AddDays(-31)); }
                catch (Exception) { friends = null; }                     // offline or signed out: the stored days still count
                await FetchSocialAsync(user.Id, today);
            }
            var check = await _badges.CheckAsync(user.Id, today, friends);
            if (check.Backfilled || check.Unlocks.Count > 0) _sync?.MarkStatsDirty(user.Id);   // friends see them (25d)
            if (check.Backfilled) HistoryFound?.Invoke();                 // the sheet takes the count (TakeHistorySheetAsync)
            else if (check.Unlocks.Count > 0) Unlocked?.Invoke(check.Unlocks);
        }
        catch (Exception e) { _log.Write(e, "Badges"); }
        finally
        {
            _gate.Release();
            Interlocked.Decrement(ref _pending);
        }
    }

    /// <summary>What badges need from the server (25d): Recruiter's invite count, and last week's board for Top of the
    /// Board, read once per finished week. Failures (offline, signed out) wait for the next time.</summary>
    private async Task FetchSocialAsync(int userId, DateOnly today)
    {
        try
        {
            if (_share is not null && await _share.GetAcceptedInvitesAsync(userId) is { } invites)
                await _badges.RecordAcceptedInvitesAsync(userId, invites, today);
        }
        catch (Exception) { /* next time */ }

        try
        {
            var lastMonday = PointsCalculator.MondayOf(today).AddDays(-7);
            if (_leaderboard is null || await _badges.HasWeekResultAsync(userId, lastMonday)) return;
            var board = await _leaderboard.GetAsync(userId, LeaderboardPeriod.LastWeek, today);
            if (board is not { IsStale: false } || board.Rows.FirstOrDefault(r => r.IsYou) is not { } me) return;
            var first = me.Rank == 1 && me.Points > 0 && board.Rows.Any(r => !r.IsYou && r.Points > 0);
            await _badges.RecordWeekResultAsync(userId, lastMonday, first);
        }
        catch (Exception) { /* next time */ }
    }
}
