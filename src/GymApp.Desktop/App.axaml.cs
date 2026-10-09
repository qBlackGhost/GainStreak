using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GymApp.Desktop.Data;
using GymApp.Desktop.Services;
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
        Services = sc.BuildServiceProvider();

        using (var db = Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
            db.Database.Migrate();
        
        Services.GetRequiredService<RememberMeService>().TryRestore();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow { DataContext = Services.GetRequiredService<MainViewModel>() };

        base.OnFrameworkInitializationCompleted();
    }
}