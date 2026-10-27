using GymApp.App;
using GymApp.App.ViewModels;
using GymApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Tests;

/// <summary>The server's "please update" banner (16f): the phones' "Update" opens the store; the desktop's
/// "Restart now" (an update already downloaded) takes its place. And the OpenStreetMap User-Agent names the platform.</summary>
[Collection(DataDirectoryCollection.Name)]
public sealed class UpdateBannerTests
{
    private static (MainViewModel Shell, FakePlatform Platform, ServiceProvider Sp) Build(bool canOpenStore)
    {
        var platform = new FakePlatform { CanOpenStorePage = canOpenStore };
        var sp = AppServices.Create(localSocial: true, platform).BuildServiceProvider();
        return (sp.GetRequiredService<MainViewModel>(), platform, sp);
    }

    [Fact] public async Task Update_ShowsWithTheNotice_AndOpensTheStore()
    {
        using var data = new TempDataDirectory();
        var (shell, platform, sp) = Build(canOpenStore: true);
        await using var _ = sp;

        Assert.False(shell.CanOpenStore);   // nothing to update yet
        shell.UpdateNotice = "This version of GainStreak is too old.";
        Assert.True(shell.CanOpenStore);

        shell.OpenStoreCommand.Execute(null);
        Assert.Equal(1, platform.StorePageOpened);
    }

    [Fact] public async Task Update_StaysHidden_WithoutAStorePage()
    {
        using var data = new TempDataDirectory();
        var (shell, _, sp) = Build(canOpenStore: false);   // the desktop, or iOS before its App Store listing
        await using var _ = sp;

        shell.UpdateNotice = "This version of GainStreak is too old.";
        Assert.False(shell.CanOpenStore);
    }

    [Fact] public async Task RestartNow_TakesUpdatesPlace()
    {
        using var data = new TempDataDirectory();
        var (shell, _, sp) = Build(canOpenStore: true);
        await using var _ = sp;

        shell.UpdateNotice = "An update is ready.";
        shell.RestartForUpdate = () => { };
        Assert.True(shell.CanRestartForUpdate);
        Assert.False(shell.CanOpenStore);
    }

    [Fact] public void TheOsmUserAgent_NamesTheVersionAndPlatform()
    {
        Assert.Equal("GainStreak/0.16.0 (Android; student project)", OsmHttp.ForPlatform("0.16.0", "Android"));

        using var data = new TempDataDirectory();
        using var sp = AppServices.Create(localSocial: true, new FakePlatform()).BuildServiceProvider();
        Assert.Equal(OsmHttp.ForPlatform(AppVersion.Current, AppVersion.PlatformName), OsmHttp.UserAgent);
        Assert.NotEqual("other", AppVersion.PlatformName);
    }
}
