using Avalonia;
using System;
using GymApp.App;
using Velopack;

namespace GymApp.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // First: the installer and updater start the app with their own arguments and expect it to exit at once.
        // Also installs an update downloaded last time (see AppUpdater) before anything opens the database.
        VelopackApp.Build().Run();
        GymApplication.Platform = new DesktopPlatform();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<GymApplication>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
