using Avalonia.Controls;
using GymApp.App;
using GymApp.App.ViewModels;
using GymApp.Client;
using GymApp.Data;
using GymApp.Services;

namespace GymApp.Tests;

/// <summary>A head for tests: the data folder follows GYMAPP_DATA_DIR (see <see cref="TempDataDirectory"/>), the token
/// lives in memory, and "Show log file" records that it was asked.</summary>
public sealed class FakePlatform : IPlatform
{
    public string DataDirectory => AppDbContext.DefaultDataDirectory;
    public string? ServerUrl => null;
    public string AppVersionDescription => "GainStreak test";
    public ITokenStore CreateTokenStore() => new MemoryTokenStore();

    public bool CanShowLogFolder { get; set; } = true;
    public int LogFolderShown { get; private set; }
    public bool FailToShowLogFolder { get; set; }

    public void ShowLogFolder()
    {
        if (FailToShowLogFolder) throw new InvalidOperationException("no file manager");
        LogFolderShown++;
    }

    public bool CanOpenStorePage { get; set; }
    public int StorePageOpened { get; private set; }
    public void OpenStorePage() => StorePageOpened++;

    public bool HasLiveWorkout { get; set; }

    /// <summary>The reminders' scheduler; none unless a test sets one.</summary>
    public INotificationScheduler Notifications { get; set; } = NoNotificationScheduler.Instance;
    public INotificationScheduler CreateNotificationScheduler() => Notifications;
    public ILocationProvider Location { get; set; } = NoLocationProvider.Instance;
    public ILocationProvider CreateLocationProvider() => Location;
    public void OnFrameworkReady() { }
    public Window CreateWindow(Control content, bool isStartupError) => throw new NotSupportedException();
    public void OnShellReady(MainViewModel shell, ErrorLog log) { }
}
