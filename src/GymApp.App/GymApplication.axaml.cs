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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.App;

/// <summary>
/// The Avalonia application every head starts. Not called "App": that's the last part of its namespace, and a head's
/// code (namespace GymApp.Desktop, GymApp.TestBench…) would read "App" as the namespace GymApp.App.
/// </summary>
public partial class GymApplication : Application
{
    private static string DataErrorText =>
        "GainStreak couldn't open its data, so it can't start. The details were saved to errors.log in " +
        $"{AppDbContext.DataDirectory}. Close any other copy of GainStreak and try again.";

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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Platform.OnFrameworkReady();
        AppDbContext.DataDirectory = Platform.DataDirectory;
        Services = AppServices.Create(LocalSocial, Platform).BuildServiceProvider();

        var log = Services.GetRequiredService<ErrorLog>();
        // The server's "please update" (426), kept until the main view exists to show it
        MainViewModel? shell = null;
        string? updateNotice = null;

        try
        {
            using (var db = Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
                db.Database.Migrate();

            // One-time points rebuild for users from before the ledger existed. Off the UI thread so the
            // async EF calls can't deadlock on the dispatcher.
            var pointsService = Services.GetRequiredService<PointsService>();
            Task.Run(() => pointsService.BackfillAllAsync()).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            // The data can't be opened (locked, corrupt, read-only...): say so instead of crashing with nothing on screen
            log.Write(e, "Startup");
            ShowStartupError(DataErrorText);
            return;
        }

        try
        {
            if (!LocalSocial)
            {
                // A token the server stopped accepting signs out to the login screen. The event can come from any
                // thread (a background upload), and signing out navigates, so it goes through the UI thread
                var accounts = Services.GetRequiredService<AccountService>();
                var api = Services.GetRequiredService<SocialApiClient>();
                api.SessionExpired += (_, _) => Dispatcher.UIThread.Post(() => _ = accounts.EndSessionAsync());
                api.UpdateRequired += (_, message) => Dispatcher.UIThread.Post(() =>
                {
                    updateNotice = message;
                    if (shell is not null) shell.UpdateNotice = message;
                });

                // Upload what's waiting at sign-in and restore, when a request gets through after being offline,
                // and every 15 minutes (the window coming to the front is wired below)
                var sync = Services.GetRequiredService<SocialSync>();
                var session = Services.GetRequiredService<SessionService>();
                session.Changed += () => { if (session.IsLoggedIn) sync.RequestSync(); };
                api.BackOnline += (_, _) => sync.RequestSync();
                sync.StartTimer();
            }
            // The Theme setting: the signed-in user's; the system's on the login screens
            var themeSession = Services.GetRequiredService<SessionService>();
            themeSession.Changed += () => AppTheme.Apply(themeSession.CurrentUser?.ThemePreference);

            // Stays signed in until logging out. Off the UI thread, like the backfill; offline it uses the cached account
            var restore = Services.GetRequiredService<IAccountService>();
            Task.Run(restore.RestoreAsync).GetAwaiter().GetResult();
            AppTheme.Apply(themeSession.CurrentUser?.ThemePreference);   // now, before the window shows, not after
        }
        catch (InvalidOperationException e) when (e.Message.Contains(ServerAddress.EnvironmentVariable))
        {
            // A release build with no (or no https) server address
            log.Write(e, "Startup");
            ShowStartupError($"GainStreak can't reach its server: {e.Message}");
            return;
        }

        var main = shell = Services.GetRequiredService<MainViewModel>();
        main.UpdateNotice ??= updateNotice;
        ErrorHandling.Install(log, message => main.GlobalError = message);
        var view = new MainView { DataContext = main };
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                var window = Platform.CreateWindow(view, isStartupError: false);
                desktop.MainWindow = window;
                if (!LocalSocial)
                {
                    var sync = Services.GetRequiredService<SocialSync>();
                    window.Activated += (_, _) => sync.RequestSync();
                }
                MainWindowCreated?.Invoke(window);
                break;
            default:
                Show(view);
                break;
        }

        Platform.OnShellReady(main, log);
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowStartupError(string text)
    {
        var view = StartupErrorView(text);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = Platform.CreateWindow(view, isStartupError: true);
        else
            Show(view);
        base.OnFrameworkInitializationCompleted();
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
