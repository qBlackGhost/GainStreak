using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Core;
using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Services;

/// <summary>Where the services say "this changed, upload it". The app's version is <see cref="SocialSync"/>; tests and
/// the TestBench's local mode use <see cref="NoSocialSync"/>. A mark never blocks or fails the caller.</summary>
public interface ISocialSyncHooks
{
    void MarkStatsDirty(int userId);
    void MarkCheckInsDirty(int userId);
    void MarkSharingDirty(int userId);
}

public sealed class NoSocialSync : ISocialSyncHooks
{
    public static readonly NoSocialSync Instance = new();
    public void MarkStatsDirty(int userId) { }
    public void MarkCheckInsDirty(int userId) { }
    public void MarkSharingDirty(int userId) { }
}

/// <summary>
/// The only code that uploads to the social server: the sharing setting, the last 31 days of check-ins and the 14-day
/// stats projection. What still has to go is kept in <see cref="SyncState"/>, so it survives a restart with no signal.
/// Uploads are retried (unlike friend writes): offline leaves the flag set; a refusal is logged in
/// <see cref="SyncState.LastError"/> and clears it, so a payload the server won't take isn't sent forever.
/// Runs for the signed-in, linked user only, one sync at a time per user.
/// </summary>
public sealed class SocialSync(
    SocialApiClient api, IDbContextFactory<AppDbContext> dbFactory, SessionService session,
    StatsProjectionService projection, TimeProvider? clock = null) : ISocialSyncHooks, IDisposable
{
    public const int CheckInDays = 31;
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private enum Flag { Stats, CheckIns, Sharing }

    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();
    // Marks for one user are written one at a time: two at once would both try to insert the user's SyncState row
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _markLocks = new();
    private readonly List<Task> _running = [];
    private readonly CancellationTokenSource _stop = new();

    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public void MarkStatsDirty(int userId) => Background(() => MarkAsync(userId, Flag.Stats));
    public void MarkCheckInsDirty(int userId) => Background(() => MarkAsync(userId, Flag.CheckIns));
    public void MarkSharingDirty(int userId) => Background(() => MarkAsync(userId, Flag.Sharing));

    /// <summary>Syncs the signed-in user in the background, if they're linked. For sign-in, restore, the window coming
    /// back to the front, the timer and the connection coming back.</summary>
    public void RequestSync()
    {
        if (session.CurrentUser is { ServerUserId: not null } user) Background(() => SyncAsync(user.Id));
    }

    /// <summary>Starts the every-15-minutes sync. Stops on <see cref="Dispose"/>.</summary>
    public void StartTimer()
    {
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(Interval, clock ?? TimeProvider.System);
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token)) RequestSync();
            }
            catch (OperationCanceledException) { /* shutting down */ }
        });
    }

    /// <summary>Waits for every background mark and sync started so far: for tests, and a clean shutdown.</summary>
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] running;
            lock (_running) running = _running.Where(t => !t.IsCompleted).ToArray();
            if (running.Length == 0) return;
            await Task.WhenAll(running);
        }
    }

    /// <summary>Uploads whatever is dirty for <paramref name="userId"/>: sharing, then check-ins, then stats. Stops at
    /// the first offline step, leaving it and the rest for next time.</summary>
    public async Task SyncAsync(int userId)
    {
        var gate = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!IsSignedInLinked(userId)) return;
            SyncState? state;
            await using (var db = await dbFactory.CreateDbContextAsync())
                state = await db.SyncStates.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId);
            if (state is null) return;

            if (state.SharingDirtySince is { } sharing && !await RunAsync(userId, Flag.Sharing, sharing, UploadSharingAsync)) return;
            if (state.CheckInsDirtySince is { } checkIns && !await RunAsync(userId, Flag.CheckIns, checkIns, UploadCheckInsAsync)) return;
            if (state.StatsDirtySince is { } stats) await RunAsync(userId, Flag.Stats, stats, UploadStatsAsync);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>One step. False if it was offline (stop here); true otherwise, the flag cleared unless it was marked again
    /// meanwhile (then the next sync sends the newer data).</summary>
    private async Task<bool> RunAsync(int userId, Flag flag, DateTime markedAt, Func<int, Task<ApiResult?>> upload)
    {
        if (!IsSignedInLinked(userId)) return false;   // signed out or switched accounts mid-sync
        var r = await upload(userId);
        if (r is { IsOffline: true }) return false;

        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.SyncStates.Where(s => s.UserId == userId);
        if (flag == Flag.CheckIns && r is { Status: 403 })
        {
            // The server doesn't know sharing is on yet (turning it on marks both, and the two can race): send the
            // setting first, and keep the check-ins for the sync after it
            var now = Now;
            await q.ExecuteUpdateAsync(s => s.SetProperty(x => x.SharingDirtySince, now));
            return false;
        }
        if (r is { IsRefused: true })
            await q.ExecuteUpdateAsync(s => s.SetProperty(x => x.LastError, $"{flag}: {r.Status} {r.Error}"));
        _ = flag switch
        {
            Flag.Sharing => await q.Where(s => s.SharingDirtySince == markedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SharingDirtySince, (DateTime?)null)),
            Flag.CheckIns => await q.Where(s => s.CheckInsDirtySince == markedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CheckInsDirtySince, (DateTime?)null)),
            _ => await q.Where(s => s.StatsDirtySince == markedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StatsDirtySince, (DateTime?)null)),
        };
        return true;
    }

    private async Task<ApiResult?> UploadSharingAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var share = await db.UserProfiles.Where(p => p.UserId == userId).Select(p => p.ShareGymCheckIns).FirstOrDefaultAsync();
        return await api.SetSharingAsync(share == true);   // never asked counts as not sharing
    }

    /// <summary>The last 31 days of logs that have a gym. Not sharing sends nothing: turning sharing off already deleted
    /// them all on the server.</summary>
    private async Task<ApiResult?> UploadCheckInsAsync(int userId)
    {
        var today = AppClock.Today;
        var from = today.AddDays(-(CheckInDays - 1));
        List<DatedCheckInRequest> checkIns;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var share = await db.UserProfiles.Where(p => p.UserId == userId).Select(p => p.ShareGymCheckIns).FirstOrDefaultAsync();
            if (share != true) return null;

            var logs = await db.WorkoutLogs.AsNoTracking()
                .Where(w => w.UserId == userId && w.GymId != null && w.Date >= from && w.Date <= today)
                .OrderBy(w => w.Date)
                .Select(w => new { w.Date, w.Gym!.OsmType, w.Gym.OsmId, w.Gym.Name, w.Gym.Latitude, w.Gym.Longitude,
                    CityOsmType = w.Gym.City.OsmType, CityOsmId = w.Gym.City.OsmId, CityName = w.Gym.City.Name })
                .ToListAsync();
            checkIns = logs.Select(l =>
            {
                // Keyed by OSM type and id, since local ids mean nothing on another phone; a custom gym's position may be
                // someone's home, so it never leaves the phone
                var isOsm = l.OsmType is not null && l.OsmId is not null;
                return new DatedCheckInRequest(l.Date,
                    isOsm ? ToContract(l.OsmType!.Value) : null, isOsm ? l.OsmId : null, l.Name,
                    ToContract(l.CityOsmType), l.CityOsmId, l.CityName,
                    isOsm ? l.Latitude : null, isOsm ? l.Longitude : null);
            }).ToList();
        }
        return await api.ReplaceCheckInsAsync(new CheckInsUploadRequest(checkIns));
    }

    private async Task<ApiResult?> UploadStatsAsync(int userId)
    {
        var days = await projection.ProjectAsync(userId, AppClock.Today);
        if (days is null) return null;
        return await api.UploadStatsAsync(new StatsUploadRequest(
            days.Select(d => new StatsDayRequest(d.Date, d.TotalPoints, d.WeekPoints, d.CurrentStreak)).ToList()));
    }

    /// <summary>Records that <paramref name="flag"/> changed now, then syncs. The time is the latest change, so a sync
    /// that started before it won't clear it.</summary>
    private async Task MarkAsync(int userId, Flag flag)
    {
        var now = Now;
        var gate = _markLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var state = await db.SyncStates.FirstOrDefaultAsync(s => s.UserId == userId);
            if (state is null)
            {
                if (!await db.Users.AnyAsync(u => u.Id == userId)) return;
                state = new SyncState { UserId = userId };
                db.SyncStates.Add(state);
            }
            switch (flag)
            {
                case Flag.Sharing: state.SharingDirtySince = now; break;
                case Flag.CheckIns: state.CheckInsDirtySince = now; break;
                default: state.StatsDirtySince = now; break;
            }
            await db.SaveChangesAsync();
        }
        finally
        {
            gate.Release();
        }
        await SyncAsync(userId);
    }

    private bool IsSignedInLinked(int userId) => session.CurrentUser is { ServerUserId: not null } u && u.Id == userId;

    private void Background(Func<Task> work)
    {
        var task = Task.Run(async () =>
        {
            try { await work(); }
            catch (Exception) { /* a failed upload is retried by the next mark or timer tick; the caller never sees it */ }
        });
        lock (_running)
        {
            _running.RemoveAll(t => t.IsCompleted);
            _running.Add(task);
        }
    }

    private static OsmType ToContract(OsmElementType t) => t switch
    {
        OsmElementType.Node => OsmType.Node,
        OsmElementType.Way => OsmType.Way,
        _ => OsmType.Relation
    };

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
