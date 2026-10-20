using System;
using System.Diagnostics;
using Avalonia;
using GymApp.Core;
using GymApp.Desktop;

namespace GymApp.TestBench;

sealed class Program
{
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
        App.LocalSocial = !settings.ServerSocial;

        App.MainWindowCreated = main =>
        {
            main.Title = "GainStreak (TestBench)";
            main.Opened += (_, _) => new AdminWindow { DataContext = new AdminViewModel(settings) }.Show(main);
        };

        AppBuilder.Configure<App>()
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
