using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GymApp.Data;
using GymApp.Services;
using GymApp.Desktop.ViewModels;
using GymApp.Desktop.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymApp.Desktop;

public partial class App : Application
{
    private static Window StartupErrorWindow() => new()
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
            Text = "GymApp couldn't open its data, so it can't start. The details were saved to errors.log in " +
                   $"{AppDbContext.DataDirectory}. Close any other copy of GymApp and try again."
        }
    };

    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var sc = new ServiceCollection();
        sc.AddDbContextFactory<AppDbContext>();
        sc.AddSingleton<SessionService>();
        sc.AddTransient<AuthService>();
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<Func<MainViewModel>>(sp => () => sp.GetRequiredService<MainViewModel>());
        sc.AddTransient<LoginViewModel>();
        sc.AddTransient<RegisterViewModel>();
        sc.AddTransient<HomeViewModel>();
        sc.AddSingleton<RememberMeService>();
        sc.AddTransient<SplitService>();
        sc.AddTransient<SplitsViewModel>();
        sc.AddTransient<SplitEditorViewModel>();
        sc.AddTransient<SplitDetailViewModel>();
        sc.AddTransient<StreakService>();
        sc.AddTransient<PointsService>();
        sc.AddTransient<WorkoutService>();
        sc.AddTransient<MissedDayService>();
        sc.AddTransient<ExerciseService>();
        sc.AddTransient<DayExerciseService>();
        sc.AddTransient<SetService>();
        sc.AddTransient<WorkoutMenuService>();
        sc.AddTransient<WorkoutsViewModel>();
        sc.AddTransient<DayViewModel>();
        sc.AddTransient<ExerciseViewModel>();
        sc.AddTransient<CustomExercisesViewModel>();
        sc.AddTransient<ProfileService>();
        sc.AddTransient<CalorieService>();
        sc.AddTransient<ProfileViewModel>();
        sc.AddSingleton(_ => new GeocodingService());   // one instance, so its one-request-per-second limit holds
        sc.AddTransient<CityService>();
        sc.AddSingleton(sp => new GymFetchService(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
        sc.AddTransient<GymService>();
        sc.AddSingleton(sp => new GymAddressLookupService(
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>(), sp.GetRequiredService<GeocodingService>()));
        sc.AddTransient<GymPickerViewModel>();
        sc.AddTransient<GymSummaryService>();
        sc.AddTransient<GymSummaryViewModel>();
        sc.AddTransient<PointsHistoryViewModel>();
        sc.AddTransient<ISocialStore, LocalSocialStore>();   // 10f may swap in a server-backed store here
        sc.AddTransient<FriendService>();
        sc.AddTransient<LeaderboardService>();
        sc.AddTransient<FriendsViewModel>();
        sc.AddTransient<LeaderboardViewModel>();
        Services = sc.BuildServiceProvider();

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
            if (desktop is not null) desktop.MainWindow = StartupErrorWindow();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        Services.GetRequiredService<RememberMeService>().TryRestore();

        var main = Services.GetRequiredService<MainViewModel>();
        ErrorHandling.Install(log, message => main.GlobalError = message);
        if (desktop is not null)
            desktop.MainWindow = new MainWindow { DataContext = main };

        base.OnFrameworkInitializationCompleted();
    }
}