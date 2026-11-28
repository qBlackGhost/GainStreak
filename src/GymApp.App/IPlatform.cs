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

    /// <summary>Phones log workouts only through the live workout on Home (Phase 18); the desktop keeps the "I went
    /// today" check-in. Decided by the head, never by the window's width.</summary>
    bool HasLiveWorkout { get; }

    /// <summary>Badges (Phase 25) are on the phones only: their screens and entry points hide elsewhere. The badges
    /// are still worked out and stored everywhere (shared services); the desktop just shows none.</summary>
    bool HasBadges => false;

    /// <summary>The food log (Phase 26): the Calories diary, the Add food sheet and Home's Add food button. The phones and, from 0.27.2, the desktop.
    /// A head without it keeps the one-number form. Decided by the head, never by the window's width.</summary>
    bool HasFoodLog => false;

    /// <summary>Changing your username (Phase 28): the phones only, as Settings' Username row and its sheet. The
    /// desktop (frozen) hides it; the TestBench turns it on. Decided by the head, never by the window's width.</summary>
    bool HasUsernameChange => false;

    /// <summary>The camera barcode scanner for the food log (Phase 26): iOS and Android have one; the desktop's is a no-op.</summary>
    IBarcodeScanner CreateBarcodeScanner() => new NoBarcodeScanner();

    /// <summary>The reminders' local notifications (16g); the desktop's does nothing.</summary>
    INotificationScheduler CreateNotificationScheduler();

    /// <summary>The position for "verified at gym" (18c); the desktop's has none (the TestBench's is a fake).</summary>
    ILocationProvider CreateLocationProvider();

    /// <summary>The workout's Live Activity on iOS (0.20.0); every other head shows none.</summary>
    ILiveActivity CreateLiveActivity() => new NoLiveActivity();

    /// <summary>The Home Screen widgets and Control Center controls on iOS (Phase 24); every other head has none.</summary>
    IHomeWidgets CreateHomeWidgets() => new NoHomeWidgets();

    /// <summary>Apple Health on iOS (Phase 20), Health Connect on Android (Phase 23); the desktop has none.</summary>
    IHealthStore CreateHealthStore() => new NoHealthStore();

    /// <summary>Friend notifications through APNs or FCM (Phase 22); the desktop has none, so Profile and Friends hide them.</summary>
    IPushNotifications CreatePushNotifications() => NoPushNotifications.Instance;

    /// <summary>Whether <see cref="Share"/> opens the system's share sheet (phones). The desktop has Copy link only.</summary>
    bool CanShare => false;

    /// <summary>The system's share sheet with <paramref name="text"/> (an invite link, Phase 21).</summary>
    void Share(string text) { }
}
