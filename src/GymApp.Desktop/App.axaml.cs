using System;
using System.Threading.Tasks;
using Avalonia;
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
        Services = sc.BuildServiceProvider();

        using (var db = Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
            db.Database.Migrate();

        // One-time points rebuild for users from before the ledger existed. Off the UI thread so the
        // async EF calls can't deadlock on the dispatcher.
        var pointsService = Services.GetRequiredService<PointsService>();
        Task.Run(() => pointsService.BackfillAllAsync()).GetAwaiter().GetResult();
        
        Services.GetRequiredService<RememberMeService>().TryRestore();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow { DataContext = Services.GetRequiredService<MainViewModel>() };

        base.OnFrameworkInitializationCompleted();
    }
}