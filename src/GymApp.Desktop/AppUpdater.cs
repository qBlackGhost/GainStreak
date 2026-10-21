using System;
using System.Net.Http;
using System.Threading.Tasks;
using GymApp.Services;
using Velopack;
using Velopack.Sources;

namespace GymApp.Desktop;

/// <summary>
/// Updates from where the app was published to (<see cref="BuildSettings.UpdateUrl"/>): a GitHub repository's
/// releases (https://github.com/owner/repo, which must be public) or any other folder of release files. A newer
/// release downloads in the background; Velopack installs it at the next start (Program.Main), or at once through
/// the restart action handed to <c>onReady</c>. Does nothing without an update folder or outside an installed copy
/// (dotnet run, the TestBench, a plain publish folder).
/// </summary>
internal static class AppUpdater
{
    /// <param name="onReady">Called off the UI thread with the new version and an action that restarts into it.</param>
    public static async Task DownloadAsync(ErrorLog log, Action<string, Action> onReady)
    {
        if (BuildSettings.UpdateUrl is not { } url) return;
        try
        {
            var manager = IsGitHubRepo(url)
                ? new UpdateManager(new GithubSource(url, accessToken: null, prerelease: false))
                : new UpdateManager(url);
            if (!manager.IsInstalled) return;
            var update = await manager.CheckForUpdatesAsync();
            if (update is null) return;
            await manager.DownloadUpdatesAsync(update);
            var target = update.TargetFullRelease;
            onReady(target.Version.ToString(), () => manager.ApplyUpdatesAndRestart(target));
        }
        catch (HttpRequestException)
        {
            // Offline or the folder is unreachable: try again next start, without filling errors.log
        }
        catch (Exception e)
        {
            log.Write(e, "Update");
        }
    }

    private static bool IsGitHubRepo(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
}
