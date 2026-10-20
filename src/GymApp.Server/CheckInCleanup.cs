using GymApp.Core;
using GymApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Server;

/// <summary>Deletes check-ins older than 31 days once a day. Friends never see past 30 days anyway; this keeps the
/// rows from piling up. The first run waits a minute so startup (and migrations) come first.</summary>
public class CheckInCleanup(IServiceScopeFactory scopes, TimeProvider clock, ILogger<CheckInCleanup> log) : BackgroundService
{
    public const int KeepDays = FriendMapRules.WindowDays + 1;

    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>One pass: removes every check-in dated before <see cref="KeepDays"/> days ago. Returns how many.</summary>
    public static Task<int> RunAsync(ServerDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        var cutoff = clock.ServerToday().AddDays(-KeepDays);
        return db.CheckIns.Where(c => c.Date < cutoff).ExecuteDeleteAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRunDelay, clock, stoppingToken);
            using var timer = new PeriodicTimer(Interval, clock);
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var deleted = await RunAsync(scope.ServiceProvider.GetRequiredService<ServerDbContext>(), clock, stoppingToken);
                    log.LogInformation("Check-in cleanup deleted {Count} rows.", deleted);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // A failed pass (database down, say) must not stop the server; tomorrow's pass catches up
                    log.LogError(e, "Check-in cleanup failed.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
    }
}
