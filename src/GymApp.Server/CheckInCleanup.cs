using GymApp.Core;
using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server;

/// <summary>Deletes check-ins older than 31 days. Friends never see past 30 days anyway; this keeps the rows from piling
/// up. It runs from the check-in uploads, at most once a day per server instance, rather than on a timer: a host that
/// scales to zero (Cloud Run, Render) stops idle instances long before a daily timer fires, and only uploads add rows.</summary>
public class CheckInCleanup(TimeProvider clock, ILogger<CheckInCleanup> log)
{
    public const int KeepDays = FriendMapRules.WindowDays + 1;

    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    // UTC ticks of the last pass on this instance; 0 before the first
    private long _lastRun;

    /// <summary>One pass: removes every check-in dated before <see cref="KeepDays"/> days ago. Returns how many.</summary>
    public static Task<int> RunAsync(ServerDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        var cutoff = clock.ServerToday().AddDays(-KeepDays);
        return db.CheckIns.Where(c => c.Date < cutoff).ExecuteDeleteAsync(ct);
    }

    /// <summary>Runs a pass unless this instance ran one within the last day. Never throws: a failed pass (database
    /// down, say) must not fail the upload that triggered it, and the next upload tries again.</summary>
    public async Task RunIfDueAsync(ServerDbContext db, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastRun);
        // Claimed before running, so two uploads at once don't both delete
        if (now - last < Interval.Ticks || Interlocked.CompareExchange(ref _lastRun, now, last) != last) return;
        try
        {
            var deleted = await RunAsync(db, clock, ct);
            log.LogInformation("Check-in cleanup deleted {Count} rows.", deleted);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Interlocked.CompareExchange(ref _lastRun, last, now);
            log.LogError(e, "Check-in cleanup failed.");
        }
    }
}
