using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GymApp.Client;
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
            _ = Task.Run(() => AppStartup.PrepareAsync(Services, LocalSocial)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                var error = t.IsFaulted ? "GainStreak couldn't start. The details were saved to errors.log." : t.Result;
                if (t.Exception is { } e) Services.GetRequiredService<ErrorLog>().Write(e, "Startup");
                host.Content = error is not null ? StartupErrorView(error) : CreateShellView();
                if (error is null) Platform.OnShellReady(_shell!, Services.GetRequiredService<ErrorLog>());
                if (error is null) Services.GetRequiredService<ReminderSync>().RequestRefresh();
            }));
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Server events that change the screen go through the UI thread: they can come from any thread (a
    /// background upload).</summary>
    private void WireUiEvents()
    {
        // The Theme setting: the signed-in user's; the system's on the login screens
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
    /// stops it.</summary>
    private void WireForeground()
    {
        if (LocalSocial || TryGetFeature(typeof(IActivatableLifetime)) is not IActivatableLifetime lifetime) return;
        var sync = Services.GetRequiredService<SocialSync>();
        lifetime.Activated += (_, e) =>
        {
            if (e.Kind != ActivationKind.Background) return;
            sync.StartTimer();
            sync.RequestSync();
            Services.GetRequiredService<ReminderSync>().RequestRefresh();   // a day may have passed (16g)
        };
        lifetime.Deactivated += (_, e) =>
        {
            if (e.Kind == ActivationKind.Background) sync.StopTimer();
        };
    }

    /// <summary>A reminder was tapped (16g): Home, where the check-in and the missed-day card are. Called by the heads
    /// from any thread; does nothing before the shell exists (the app is starting on Home anyway).</summary>
    public static void OpenFromReminder() => Dispatcher.UIThread.Post(() =>
    {
        if (Current is GymApplication { _shell: { IsSignedInScreen: true } shell }) shell.ShowHome();
    });

    /// <summary>The shell, once <see cref="AppStartup"/> has run. On the UI thread.</summary>
    private MainView CreateShellView()
    {
        AppTheme.Apply(Services.GetRequiredService<SessionService>().CurrentUser?.ThemePreference);
        var main = _shell = Services.GetRequiredService<MainViewModel>();
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
