using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using GymApp.App;
using GymApp.App.ViewModels;
using GymApp.Client;
using GymApp.Data;
using GymApp.Desktop.Views;
using GymApp.Services;

namespace GymApp.Desktop;

/// <summary>macOS and Windows: the data folder under LocalApplicationData, the token in a file, Finder or Explorer
/// for the log, Velopack for updates.</summary>
public sealed class DesktopPlatform : IPlatform
{
    public string DataDirectory => AppDbContext.DefaultDataDirectory;

    public string? ServerUrl => BuildSettings.ServerUrl;

    public string AppVersionDescription => AppVersion.Description;

    public ITokenStore CreateTokenStore() => new FileTokenStore(Path.Combine(DataDirectory, "session.token"));

    public bool CanShowLogFolder => true;

    public void ShowLogFolder()
    {
        var opener = OperatingSystem.IsMacOS() ? "open" : OperatingSystem.IsWindows() ? "explorer" : "xdg-open";
        Process.Start(new ProcessStartInfo(opener, $"\"{DataDirectory}\"") { UseShellExecute = false });
    }

    public bool CanOpenStorePage => false;

    public void OpenStorePage() { }

    public void OnFrameworkReady() => MacDockIcon.Apply();

    public Window CreateWindow(Control content, bool isStartupError) => isStartupError
        ? new Window
        {
            Title = "GainStreak",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Content = content
        }
        : new MainWindow { Content = content };

    public void OnShellReady(MainViewModel shell, ErrorLog log) =>
        _ = AppUpdater.DownloadAsync(log, (version, restart) => Dispatcher.UIThread.Post(() =>
        {
            shell.UpdateNotice = $"GainStreak {version} is ready. Restart now, or it installs the next time you open the app.";
            shell.RestartForUpdate = restart;
        }));
}
