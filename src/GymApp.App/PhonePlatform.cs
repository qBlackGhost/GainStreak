using System;
using Avalonia.Controls;
using GymApp.App.ViewModels;
using GymApp.Client;
using GymApp.Services;

namespace GymApp.App;

/// <summary>What the Android and iOS heads have in common: no folders to open, no windows, the stores install
/// updates. Each head adds its data folder and token store.</summary>
public abstract class PhonePlatform(string? serverUrl) : IPlatform
{
    public abstract string DataDirectory { get; }

    public string? ServerUrl => serverUrl is { Length: > 0 } ? serverUrl : null;

    public string AppVersionDescription => AppVersion.Description;

    public abstract ITokenStore CreateTokenStore();

    public bool CanShowLogFolder => false;

    public void ShowLogFolder() => throw new NotSupportedException("Phones have no folder to open; Feedback attaches the log.");

    /// <summary>The 426 banner's "Update" button (16f): the head opens its store listing.</summary>
    public abstract bool CanOpenStorePage { get; }

    public abstract void OpenStorePage();

    public virtual void OnFrameworkReady() { }

    public Window CreateWindow(Control content, bool isStartupError) =>
        throw new NotSupportedException("Phones show a single view, not windows.");

    public virtual void OnShellReady(MainViewModel shell, ErrorLog log) { }

    public bool HasLiveWorkout => true;

    public abstract INotificationScheduler CreateNotificationScheduler();

    public abstract ILocationProvider CreateLocationProvider();

    /// <summary>Debug builds: point the server address at <paramref name="debugServerUrl"/> (the Mac as the emulator or
    /// Simulator sees it) through the same override the desktop's GYMAPP_SERVER_URL uses. Call before Avalonia starts.</summary>
    public static void UseDebugServer(string debugServerUrl)
    {
#if DEBUG
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ServerAddress.EnvironmentVariable)))
            Environment.SetEnvironmentVariable(ServerAddress.EnvironmentVariable, debugServerUrl);
#endif
    }
}
