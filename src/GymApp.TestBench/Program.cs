using System;
using System.Diagnostics;
using Avalonia;
using GymApp.App;
using GymApp.Core;
using GymApp.Desktop;

namespace GymApp.TestBench;

sealed class Program
{
    /// <summary>The fake location the admin window sets (18c).</summary>
    public static readonly BenchLocation Location = new();

    [STAThread]
    public static void Main(string[] args)
    {
        // After "Reset data" the previous run starts this one and exits: let it close the database first
        if (args is ["--after", var pid] && int.TryParse(pid, out var id))
            WaitForExit(id);

        var settings = BenchSettings.Load();
        if (!System.IO.Directory.Exists(BenchSettings.DataDir) && settings.PendingReset == DataReset.None)
            settings.PendingReset = DataReset.CopyReal;   // first run: start from a copy of the real data
        settings.ApplyPendingReset();

        // Before anything touches the app: its own data folder and the moved clock
        Environment.SetEnvironmentVariable("GYMAPP_DATA_DIR", BenchSettings.DataDir);
        AppClock.Offset = TimeSpan.FromDays(settings.DayOffset);
        // Local accounts and friends by default: the server refuses a clock more than 2 days off
        GymApplication.LocalSocial = !settings.ServerSocial;
        GymApplication.Platform = new DesktopPlatform { HasLiveWorkout = settings.PhoneWorkout, HasBadges = true, HasUsernameChange = true, Location = Location };

        GymApplication.MainWindowCreated = main =>
        {
            main.Title = "GainStreak (TestBench)";
            // --seed-demo: load the presentation's data, write the outcome next to the data and quit
            if (Array.IndexOf(args, "--seed-demo") >= 0)
                main.Opened += async (_, _) =>
                {
                    string result;
                    try { result = await DemoData.LoadAsync(GymApplication.Services); }
                    catch (Exception e) { result = "Failed: " + e; }
                    System.IO.File.WriteAllText(System.IO.Path.Combine(BenchSettings.Root, "seed.log"), result);
                    // Opened can fire before the lifetime has finished starting, and Shutdown then crashes; the data is
                    // saved by now, so just leave
                    Environment.Exit(result.StartsWith("Failed") ? 1 : 0);
                };
            main.Opened += (_, _) => new AdminWindow { DataContext = new AdminViewModel(settings) }.Show(main);
        };

        AppBuilder.Configure<GymApplication>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    private static void WaitForExit(int pid)
    {
        try { Process.GetProcessById(pid).WaitForExit(TimeSpan.FromSeconds(10)); }
        catch (ArgumentException) { /* already gone */ }
    }
}
