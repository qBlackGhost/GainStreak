using GymApp.App;
using GymApp.App.ViewModels;
using GymApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Tests;

/// <summary>The startup the phones run behind the splash (16c): database, backfill and restore off the UI thread, then
/// the shell on the right screen. Run in the TestBench's local mode, which needs no server.</summary>
[Collection(DataDirectoryCollection.Name)]
public sealed class AppStartupTests
{
    private static ServiceProvider Build() => AppServices.Create(localSocial: true, new FakePlatform()).BuildServiceProvider();

    [Fact] public async Task FirstStart_OpensTheDatabase_AndShowsLogin()
    {
        using var data = new TempDataDirectory();
        await using var sp = Build();

        Assert.Null(await Task.Run(() => AppStartup.PrepareAsync(sp, localSocial: true)));
        Assert.IsType<LoginViewModel>(sp.GetRequiredService<MainViewModel>().Current);
    }

    [Fact] public async Task Restart_WithASavedSession_ShowsHome()
    {
        using var data = new TempDataDirectory();
        await using (var first = Build())
        {
            Assert.Null(await AppStartup.PrepareAsync(first, localSocial: true));
            Assert.True((await first.GetRequiredService<IAccountService>().RegisterAsync("alice", "alice@example.com", "correct horse 1")).Success);
        }

        await using var restarted = Build();
        Assert.Null(await Task.Run(() => AppStartup.PrepareAsync(restarted, localSocial: true)));
        Assert.True(restarted.GetRequiredService<SessionService>().IsLoggedIn);
        Assert.IsNotType<LoginViewModel>(restarted.GetRequiredService<MainViewModel>().Current);
    }

    [Fact] public async Task DataThatCantOpen_GivesTheMessage_AndLogsIt()
    {
        using var data = new TempDataDirectory();
        Directory.CreateDirectory(Path.Combine(data.Path, "gymapp.db"));   // a folder where the database file goes
        await using var sp = Build();

        var error = await AppStartup.PrepareAsync(sp, localSocial: true);

        Assert.NotNull(error);
        Assert.Contains("couldn't open its data", error);
        Assert.Contains(data.Path, error);
        Assert.Contains("Startup", File.ReadAllText(Path.Combine(data.Path, ErrorLog.FileName)));
    }
}
