using System;
using System.Threading.Tasks;
using GymApp.Client;
using GymApp.Data;
using GymApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.App;

/// <summary>
/// What runs before the app shows: the database, the one-time points backfill, the server sync wiring and the session
/// restore. Safe off the UI thread, which is where the phones run it (behind the splash, so iOS sees a first frame in
/// time); the desktop runs it before its window opens.
/// </summary>
public static class AppStartup
{
    /// <returns>Null when the app is ready, or the message to show instead of it. The cause is in errors.log.</returns>
    public static async Task<string?> PrepareAsync(IServiceProvider services, bool localSocial)
    {
        var log = services.GetRequiredService<ErrorLog>();
        var platform = services.GetRequiredService<IPlatform>();
        try
        {
            await using (var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
                await db.Database.MigrateAsync();

            // One-time points rebuild for users from before the ledger existed
            await services.GetRequiredService<PointsService>().BackfillAllAsync();
        }
        catch (Exception e)
        {
            // The data can't be opened (locked, corrupt, read-only...): say so instead of crashing with nothing on screen
            log.Write(e, "Startup");
            return "GainStreak couldn't open its data, so it can't start. The details were saved to errors.log in " +
                   $"{platform.DataDirectory}. Close any other copy of GainStreak and try again.";
        }

        try
        {
            if (!localSocial)
            {
                // Upload what's waiting at sign-in and restore, when a request gets through after being offline,
                // and every 15 minutes (the window or app coming to the front is wired by GymApplication)
                var api = services.GetRequiredService<SocialApiClient>();
                var sync = services.GetRequiredService<SocialSync>();
                var session = services.GetRequiredService<SessionService>();
                session.Changed += () => { if (session.IsLoggedIn) sync.RequestSync(); };
                api.BackOnline += (_, _) => sync.RequestSync();
                sync.StartTimer();
            }

            // Stays signed in until logging out; offline it uses the cached account
            await services.GetRequiredService<IAccountService>().RestoreAsync();
            return null;
        }
        catch (InvalidOperationException e) when (e.Message.Contains(ServerAddress.EnvironmentVariable))
        {
            // A release build with no (or no https) server address
            log.Write(e, "Startup");
            return $"GainStreak can't reach its server: {e.Message}";
        }
    }
}
