using Avalonia.Controls;
using GymApp.App.ViewModels;
using GymApp.Client;
using GymApp.Services;

namespace GymApp.App;

/// <summary>
/// What differs between the heads (desktop, Android, iOS), so the shared code never asks the system itself. Each
/// head sets <see cref="GymApplication.Platform"/> before Avalonia starts.
/// </summary>
public interface IPlatform
{
    /// <summary>Where the database, errors.log and the inbox live. Desktop: <c>AppDbContext.DefaultDataDirectory</c>
    /// (GYMAPP_DATA_DIR still overrides it there).</summary>
    string DataDirectory { get; }

    /// <summary>The server address the build was published with, or null (a Debug build uses localhost).</summary>
    string? ServerUrl { get; }

    /// <summary>"GainStreak 0.15.6 on Darwin … (Arm64)", stamped on errors.log entries and error reports.</summary>
    string AppVersionDescription { get; }

    /// <summary>The stored session token. Desktop: a file only this user can read; phones: the Keychain or Keystore.</summary>
    ITokenStore CreateTokenStore();

    /// <summary>Whether "Show log file" can open the data folder (Finder or Explorer). False on phones.</summary>
    bool CanShowLogFolder { get; }

    /// <summary>Opens the data folder. Throws when it can't; the caller says where the log is instead.</summary>
    void ShowLogFolder();

    /// <summary>Whether the "please update" banner can open the app's store page. False on desktop (Velopack updates).</summary>
    bool CanOpenStorePage { get; }

    void OpenStorePage();

    /// <summary>Called first on the UI thread, before any window or view exists. Desktop: the macOS Dock icon.</summary>
    void OnFrameworkReady();

    /// <summary>Desktop only (the classic lifetime): the window around <paramref name="content"/>, the main view or,
    /// with <paramref name="isStartupError"/>, the message that the app can't start.</summary>
    Window CreateWindow(Control content, bool isStartupError);

    /// <summary>Called once the shell exists. Desktop: starts the update download, which offers "Restart now"
    /// through <see cref="MainViewModel.RestartForUpdate"/>.</summary>
    void OnShellReady(MainViewModel shell, ErrorLog log);

    /// <summary>The reminders' local notifications (16g); the desktop's does nothing.</summary>
    INotificationScheduler CreateNotificationScheduler();
}
