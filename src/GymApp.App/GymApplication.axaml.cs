using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GymApp.Client;
using GymApp.Contracts;
using GymApp.Data;
using GymApp.Services;
using GymApp.Social;
using GymApp.App.ViewModels;
using GymApp.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.App;

/// <summary>
/// The Avalonia application every head starts. Not called "App": that's the last part of its namespace, and a head's
/// code (namespace GymApp.Desktop, GymApp.TestBench…) would read "App" as the namespace GymApp.App.
/// </summary>
public partial class GymApplication : Application
{
    private static Control StartupErrorView(string text) => new TextBlock
    {
        Margin = new Thickness(24),
        TextWrapping = TextWrapping.Wrap,
        Text = text
    };

    /// <summary>The head the app runs in. Set by the head before Avalonia starts.</summary>
    public static IPlatform Platform { get; set; } = null!;

    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>Called with the main window once it exists (desktop only). The TestBench uses it to open its admin window.</summary>
    public static Action<Window>? MainWindowCreated { get; set; }

    /// <summary>Accounts and friends on this computer only (<see cref="LocalAccountService"/>, the local
    /// <see cref="FriendService"/>) instead of the social server. Set by the TestBench before startup; the app itself
    /// always uses the server.</summary>
    public static bool LocalSocial { get; set; }

    // The server's "please update" (426), kept until the shell exists to show it
    private MainViewModel? _shell;
    private string? _updateNotice;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Platform.OnFrameworkReady();
        AppDbContext.DataDirectory = Platform.DataDirectory;
        Services = AppServices.Create(LocalSocial, Platform).BuildServiceProvider();
        WireUiEvents();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The desktop opens its window once everything is ready, as it always has. Off the UI thread so the
            // async EF calls can't deadlock on the dispatcher
            var error = Task.Run(() => AppStartup.PrepareAsync(Services, LocalSocial)).GetAwaiter().GetResult();
            if (error is not null)
                desktop.MainWindow = Platform.CreateWindow(StartupErrorView(error), isStartupError: true);
            else
            {
                var window = Platform.CreateWindow(CreateShellView(), isStartupError: false);
                desktop.MainWindow = window;
                if (!LocalSocial) window.Activated += (_, _) => Services.GetRequiredService<SocialSync>().RequestSync();
                if (Platform.HasLiveWorkout)
                {
                    // The TestBench's live workout: the window going to the back stands in for the phone's background
                    var watcher = Services.GetRequiredService<LiveWorkoutWatcher>();
                    window.Activated += (_, _) => _ = watcher.CameToForegroundAsync();
                    window.Deactivated += (_, _) => _ = watcher.WentToBackgroundAsync();
                    StartWorkoutTimer();
                }
                MainWindowCreated?.Invoke(window);
                Platform.OnShellReady(_shell!, Services.GetRequiredService<ErrorLog>());
            }
        }
        else
        {
            // The phones: a first frame at once (iOS ends an app that takes too long to draw one), then the app
            PhoneTouch.Install();
            var host = new ContentControl { Content = new SplashView() };
            // MainView keeps clear of the notch and home indicator itself, so the tab bar can reach behind the latter
            TopLevel.SetAutoSafeAreaPadding(host, false);
            Show(host);
            WireForeground();
            WirePush();
            _ = Task.Run(() => AppStartup.PrepareAsync(Services, LocalSocial)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                var error = t.IsFaulted ? "GainStreak couldn't start. The details were saved to errors.log." : t.Result;
                if (t.Exception is { } e) Services.GetRequiredService<ErrorLog>().Write(e, "Startup");
                host.Content = error is not null ? StartupErrorView(error) : CreateShellView();
                if (error is null) Platform.OnShellReady(_shell!, Services.GetRequiredService<ErrorLog>());
                if (error is null) Services.GetRequiredService<ReminderSync>().RequestRefresh();
                if (error is null) _ = Services.GetRequiredService<LiveWorkoutWatcher>().CameToForegroundAsync();   // a cold start
            }));
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Server events that change the screen go through the UI thread: they can come from any thread (a
    /// background upload).</summary>
    private void WireUiEvents()
    {
        // The Theme setting: the signed-in user's; dark on the login screens (0.27.1)
        var session = Services.GetRequiredService<SessionService>();
        session.Changed += () => AppTheme.Apply(session.CurrentUser?.ThemePreference);
        if (LocalSocial) return;

        // A token the server stopped accepting signs out to the login screen
        var accounts = Services.GetRequiredService<AccountService>();
        var api = Services.GetRequiredService<SocialApiClient>();
        api.SessionExpired += (_, _) => Dispatcher.UIThread.Post(() => _ = accounts.EndSessionAsync());
        api.UpdateRequired += (_, message) => Dispatcher.UIThread.Post(() =>
        {
            _updateNotice = message;
            if (_shell is not null) _shell.UpdateNotice = message;
        });
    }

    /// <summary>The phones: coming back to the front syncs and restarts the 15-minute timer; going to the background
    /// stops it. The live workout (18b) hears both: the nudge, the return popup, the 4-hour stop.</summary>
    private void WireForeground()
    {
        if (TryGetFeature(typeof(IActivatableLifetime)) is not IActivatableLifetime lifetime) return;
        var watcher = Services.GetRequiredService<LiveWorkoutWatcher>();
        var health = Services.GetRequiredService<HealthSync>();
        var sync = LocalSocial ? null : Services.GetRequiredService<SocialSync>();
        var push = Services.GetRequiredService<IPushRegistration>();
        var widgets = Services.GetRequiredService<WidgetSync>();
        lifetime.Activated += (_, e) =>
        {
            // An invite link the phone opened in the app (Phase 21): a Universal Link on iOS, an App Link on Android.
            // A tapped Home Screen widget or control (Phase 24) comes the same way, as a gainstreak:// link
            if (e is ProtocolActivatedEventArgs { Kind: ActivationKind.OpenUri, Uri: { } link }) OpenLink(link);
            if (e.Kind != ActivationKind.Background) return;
            _ = watcher.CameToForegroundAsync();
            health.RequestSync();   // Apple Health (Phase 20)
            push.RequestSync();   // friend notifications (Phase 22): the permission may have changed in Settings
            Services.GetRequiredService<BadgeSync>().RequestRefresh(withFriends: true);   // a day may have passed (Phase 25)
            if (sync is null) return;
            sync.StartTimer();
            sync.RequestSync();
            Services.GetRequiredService<ReminderSync>().RequestRefresh();   // a day may have passed (16g)
        };
        lifetime.Deactivated += (_, e) =>
        {
            if (e.Kind != ActivationKind.Background) return;
            _ = watcher.WentToBackgroundAsync();
            health.RequestSync();   // a workout, cardio or weigh-in from this visit goes to Health
            sync?.StopTimer();
            widgets.RequestRefresh();   // the Home Screen widgets show what this visit changed (Phase 24)
        };
        StartWorkoutTimer();
    }

    /// <summary>Once a minute while the app runs: a live workout past 4 hours or midnight ends by itself (18b).</summary>
    private void StartWorkoutTimer()
    {
        var watcher = Services.GetRequiredService<LiveWorkoutWatcher>();
        new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => _ = watcher.TickAsync()).Start();
    }

    /// <summary>A reminder was tapped (16g): Home, where the check-in and the missed-day card are. Called by the heads
    /// from any thread; does nothing before the shell exists (the app is starting on Home anyway).</summary>
    public static void OpenFromReminder() => Dispatcher.UIThread.Post(() =>
    {
        if (Current is GymApplication { _shell: { IsSignedInScreen: true } shell }) shell.ShowHome();
    });

    /// <summary>A link that came while the app was still starting (a cold start from a link): opened with the shell.</summary>
    private static Uri? _linkBeforeShell;

    /// <summary>A link the phone opened in the app (Phase 21): an invite opens Friends' confirm card. Called from any
    /// thread; before the shell exists the link waits for it.</summary>
    public static void OpenLink(Uri link) => Dispatcher.UIThread.Post(() =>
    {
        if (Current is GymApplication { _shell: { } shell }) shell.OpenLink(link);
        else _linkBeforeShell = link;
    });

    /// <summary>Friend notifications (Phase 22): the server's copy of the token follows sign-ins and token changes.
    /// The phones only; the desktop's push is unsupported anyway.</summary>
    private void WirePush()
    {
        var push = Services.GetRequiredService<IPushRegistration>();
        Services.GetRequiredService<IPushNotifications>().TokenChanged += push.RequestSync;
        Services.GetRequiredService<SessionService>().Changed += push.RequestSync;
    }

    /// <summary>A tapped notification's kind that came while the app was still starting (a cold start).</summary>
    private static PushKind? _pushBeforeShell;

    /// <summary>A friend notification was tapped (Phase 22). <paramref name="kind"/> is the push's <c>kind</c> data
    /// (<see cref="PushPayload.Kind"/>); anything else is ignored. Called by the heads from any thread; before the shell
    /// exists it waits for it.</summary>
    public static void OpenFromPush(string? kind)
    {
        if (PushPayload.Parse(kind) is not { } parsed) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (Current is GymApplication { _shell: { } shell }) shell.OpenFromPush(parsed);
            else _pushBeforeShell = parsed;
        });
    }

    /// <summary>A friend notification came while the app is open (Phase 22). True when the head should show it; false
    /// when the user is on Inbox or Friends, which reload instead. Called from any thread.</summary>
    public static async Task<bool> PushArrivedAsync(string? kind)
    {
        if (PushPayload.Parse(kind) is not { } parsed) return true;
        return await Dispatcher.UIThread.InvokeAsync(() =>
            Current is not GymApplication { _shell: { } shell } || shell.PushArrived(parsed));
    }

    /// <summary>The shell, once <see cref="AppStartup"/> has run. On the UI thread.</summary>
    private MainView CreateShellView()
    {
        AppTheme.Apply(Services.GetRequiredService<SessionService>().CurrentUser?.ThemePreference);
        var main = _shell = Services.GetRequiredService<MainViewModel>();
        if (_linkBeforeShell is { } waiting)
        {
            _linkBeforeShell = null;
            main.OpenLink(waiting);
        }
        if (_pushBeforeShell is { } tapped)
        {
            _pushBeforeShell = null;
            main.OpenFromPush(tapped);
        }
        main.UpdateNotice ??= _updateNotice;
        ErrorHandling.Install(Services.GetRequiredService<ErrorLog>(), message => main.GlobalError = message);
        return new MainView { DataContext = main };
    }

    /// <summary>The phones' lifetimes: iOS shows one view, Android hands it to its activity through a factory.</summary>
    private void Show(Control view)
    {
        switch (ApplicationLifetime)
        {
            case ISingleViewApplicationLifetime single:
                single.MainView = view;
                break;
            case IActivityApplicationLifetime activity:
                activity.MainViewFactory = () => view;
                break;
        }
    }
}
