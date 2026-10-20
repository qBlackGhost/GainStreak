using GymApp.Core;
using GymApp.Data;
using GymApp.Desktop;
using GymApp.Services;
using GymApp.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Tests;

/// <summary>The app's wiring: every view model and service resolves in both modes, and the TestBench's local mode still
/// runs the everyday flows with no server at all.</summary>
[Collection(DataDirectoryCollection.Name)]
public sealed class AppServicesTests
{
    private static ServiceProvider Build(bool localSocial) =>
        AppServices.Create(localSocial).BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryRegistration_Resolves(bool localSocial)
    {
        using var sp = Build(localSocial);   // ValidateOnBuild throws on any missing dependency
        Assert.NotNull(sp.GetService<IAccountService>());
    }

    [Fact] public void ServerMode_UsesTheServer_AndRegistersNoLocalSocialServices()
    {
        var sc = AppServices.Create(localSocial: false);
        Assert.DoesNotContain(sc, d => d.ServiceType == typeof(ISocialStore));
        Assert.DoesNotContain(sc, d => d.ServiceType == typeof(AuthService));
        Assert.DoesNotContain(sc, d => d.ServiceType == typeof(RememberMeService));
        Assert.Contains(sc, d => d.ServiceType == typeof(ISocialSyncHooks));

        using var sp = Build(false);
        Assert.IsType<RemoteFriendService>(sp.GetRequiredService<IFriendService>());
        Assert.IsType<RemoteFriendCheckInSource>(sp.GetRequiredService<IFriendCheckInSource>());
    }

    [Fact] public async Task LocalMode_RunsTheEverydayFlows_WithNoServer()
    {
        using var data = new TempDataDirectory();
        using var sp = Build(true);
        Assert.Null(sp.GetService<ISocialSyncHooks>());   // nothing uploads
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await db.Database.MigrateAsync();

        var accounts = sp.GetRequiredService<IAccountService>();
        var session = sp.GetRequiredService<SessionService>();
        var friends = sp.GetRequiredService<IFriendService>();
        Assert.IsType<FriendService>(friends);

        Assert.True((await accounts.RegisterAsync("bob", "bob@example.com", "correct horse 1")).Success);
        var bob = session.CurrentUser!.Id;
        await accounts.LogOutAsync();
        Assert.True((await accounts.RegisterAsync("alice", "alice@example.com", "correct horse 1")).Success);
        var alice = session.CurrentUser!.Id;

        // A workout, with its points, through the same services the app uses
        var today = AppClock.Today;
        new TestDb(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()).ReadyToCheckIn(alice, today);
        var log = await sp.GetRequiredService<WorkoutService>().LogTodayAsync(alice);
        Assert.True(log.Success, log.Error);
        Assert.True(log.Points!.NewTotal > 0);

        // Friends and the leaderboard, with the rules run locally
        Assert.True((await friends.SendRequestAsync(alice, bob)).Success);
        Assert.True((await friends.AcceptAsync(bob, alice)).Success);
        var board = (await sp.GetRequiredService<ILeaderboardService>().GetAsync(alice, LeaderboardPeriod.ThisWeek, today))!;
        Assert.Equal(["alice", "bob"], board.Rows.Select(r => r.Username));
        Assert.False(board.IsStale);

        // The map reads local check-ins; nothing to show yet, and nothing stale
        var map = await sp.GetRequiredService<FriendMapService>().GetAsync(bob, null, null, null, MapRange.Month, MapMode.Latest, FriendSort.Recent, today);
        Assert.False(map.IsStale);

        // Signed in again after a restart, from the remember-me file
        using var restarted = Build(true);
        Assert.True(await restarted.GetRequiredService<IAccountService>().RestoreAsync());
        Assert.Equal(alice, restarted.GetRequiredService<SessionService>().CurrentUser!.Id);
    }
}
