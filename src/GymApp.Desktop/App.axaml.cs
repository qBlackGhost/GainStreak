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
using GymApp.Desktop.ViewModels;
using GymApp.Desktop.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Desktop;

public partial class App : Application
{
    private static string DataErrorText =>
        "GymApp couldn't open its data, so it can't start. The details were saved to errors.log in " +
        $"{AppDbContext.DataDirectory}. Close any other copy of GymApp and try again.";

    private static Window StartupErrorWindow(string text) => new()
    {
        Title = "GymApp",
        Width = 460,
        SizeToContent = SizeToContent.Height,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        CanResize = false,
        Content = new TextBlock
        {
            Margin = new Thickness(24),
            TextWrapping = TextWrapping.Wrap,
            Text = text
        }
    };

    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>Called with the main window once it exists. The TestBench uses it to open its admin window.</summary>
    public static Action<Window>? MainWindowCreated { get; set; }

    /// <summary>Accounts and friends on this computer only (<see cref="LocalAccountService"/>, the local
    /// <see cref="FriendService"/>) instead of the social server. Set by the TestBench before startup; the app itself
    /// always uses the server.</summary>
    public static bool LocalSocial { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = AppServices.Create(LocalSocial).BuildServiceProvider();

        var log = new ErrorLog(AppDbContext.DataDirectory);
        var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

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
            // The data can't be opened (locked, corrupt, read-only...): say so instead of crashing with no window
            log.Write(e, "Startup");
            if (desktop is not null) desktop.MainWindow = StartupErrorWindow(DataErrorText);
            base.OnFrameworkInitializationCompleted();
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

                // Upload what's waiting at sign-in and restore, when a request gets through after being offline,
                // and every 15 minutes (the window coming to the front is wired below)
                var sync = Services.GetRequiredService<SocialSync>();
                var session = Services.GetRequiredService<SessionService>();
                session.Changed += () => { if (session.IsLoggedIn) sync.RequestSync(); };
                api.BackOnline += (_, _) => sync.RequestSync();
                sync.StartTimer();
            }
            // Stays signed in until logging out. Off the UI thread, like the backfill; offline it uses the cached account
            var restore = Services.GetRequiredService<IAccountService>();
            Task.Run(restore.RestoreAsync).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException e) when (e.Message.Contains(ServerAddress.EnvironmentVariable))
        {
            // A release build with no (or no https) server address
            log.Write(e, "Startup");
            if (desktop is not null) desktop.MainWindow = StartupErrorWindow($"GymApp can't reach its server: {e.Message}");
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var main = Services.GetRequiredService<MainViewModel>();
        ErrorHandling.Install(log, message => main.GlobalError = message);
        if (desktop is not null)
        {
            desktop.MainWindow = new MainWindow { DataContext = main };
            if (!LocalSocial)
            {
                var sync = Services.GetRequiredService<SocialSync>();
                desktop.MainWindow.Activated += (_, _) => sync.RequestSync();
            }
            MainWindowCreated?.Invoke(desktop.MainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }
}